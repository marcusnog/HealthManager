using System.Globalization;
using System.Text.Json;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace HealthManager.Application;

public sealed class ReportSourcesService(IApplicationDbContext db, ITenantProvider tenant)
{
    public async Task<SourcePage> ReadAsync(string source, DateOnly from, DateOnly to, int page, CancellationToken ct)
    {
        var definition = ReportCatalog.Source(source);
        ReportDates.Validate(from, to);
        if (page < 1 || page > 1000000) throw new InvalidOperationException("Página inválida.");
        var clinic = TenantGuard.RequireClinicId(tenant);
        var records = await db.ReportSourceRecords.AsNoTracking().Where(x => x.ClinicId == clinic && x.Source == source && x.Date >= from && x.Date <= to)
            .OrderBy(x => x.Date).ThenBy(x => x.Reference).Skip((page - 1) * 100).Take(101).ToListAsync(ct);
        var lines = new List<string> { string.Join(';', definition.Header) };
        foreach (var record in records.Take(100))
        {
            var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(record.ValuesJson)!;
            string[] cells = [record.Reference, record.Date.ToString("yyyy-MM-dd"), .. definition.Fields.Select(f => fields.GetValueOrDefault(f.Key, ""))];
            lines.Add(string.Join(';', cells.Select(x => "\"" + x.Replace("\"", "\"\"") + "\"")));
        }
        return new(string.Join("\n", lines), records.Count > 100);
    }

    public async Task<int> ImportAsync(string source, ReportCsv request, CancellationToken ct)
    {
        var clinic = TenantGuard.RequireClinicId(tenant);
        var definition = ReportCatalog.Source(source);
        if (string.IsNullOrWhiteSpace(request.Csv) || request.Csv.Length > 1_000_000)
            throw new InvalidOperationException("O CSV deve conter até um milhão de caracteres.");
        using var parser = new TextFieldParser(new StringReader(request.Csv.TrimStart('\uFEFF'))) { Delimiters = [";"] };
        var batch = new Dictionary<string, SourceLine>();
        try
        {
            if (!(parser.ReadFields() ?? []).SequenceEqual(definition.Header))
                throw new InvalidOperationException("Confira o cabeçalho e a ordem das colunas do modelo.");
            while (!parser.EndOfData)
            {
                ct.ThrowIfCancellationRequested();
                var line = parser.LineNumber;
                var cells = parser.ReadFields()!;
                string Error(string msg) => $"Linha {line}: {msg}.";
                if (cells.Length != definition.Header.Length || cells[0].Length is 0 or > 120 || cells[0].Any(char.IsControl))
                    throw new InvalidOperationException(Error("referência ou número de colunas inválido"));
                var date = ParseDate(cells[1]) ?? throw new InvalidOperationException(Error("data inválida, use AAAA-MM-DD"));
                var values = new Dictionary<string, string>();
                for (var i = 0; i < definition.Fields.Length; i++)
                {
                    var field = definition.Fields[i];
                    var value = cells[i + 2];
                    if (value.Length == 0 && !field.Required) continue;
                    values[field.Key] = Normalize(field, value) ?? throw new InvalidOperationException(Error($"valor inválido para {field.Key}"));
                }
                var row = new SourceLine(date, values, cells[0]);
                if (definition.Fields.Any(f => f.Type == "data" && f.Key != "execucao" && row.Day(f.Key) < date) ||
                    (source == "horarios" && string.CompareOrdinal(row.Text("inicio"), row.Text("chegada")) < 0) ||
                    (source == "reservas" && row.Number("valor") <= 0) ||
                    (source == "programas" && (row.Day("inicio") > row.Day("vencimento") || row.Day("abandono") < row.Day("inicio") || row.Day("abandono") > row.Day("vencimento"))))
                    throw new InvalidOperationException(Error("valores incompatíveis com a sequência do registro"));
                if (!batch.TryAdd(cells[0], row) || batch.Count > 1000)
                    throw new InvalidOperationException(Error("referência repetida ou limite de 1.000 linhas excedido"));
            }
        }
        catch (MalformedLineException ex) { throw new InvalidOperationException($"CSV inválido na linha {ex.LineNumber}."); }
        if (batch.Count == 0) throw new InvalidOperationException("Informe pelo menos um registro.");
        var refs = batch.Keys.ToArray();
        var existing = await db.ReportSourceRecords.Where(x => x.ClinicId == clinic && x.Source == source && refs.Contains(x.Reference))
            .ToDictionaryAsync(x => x.Reference, ct);
        foreach (var (reference, row) in batch)
        {
            var json = JsonSerializer.Serialize(row.Fields);
            if (existing.TryGetValue(reference, out var record))
            {
                record.Date = row.Date;
                record.ValuesJson = json;
            }
            else db.ReportSourceRecords.Add(new() { ClinicId = clinic, Source = source, Reference = reference, Date = row.Date, ValuesJson = json });
        }
        await db.SaveChangesAsync(ct);
        return batch.Count;
    }

    private static DateOnly? ParseDate(string value) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var date) && date.Year is >= 1900 and <= 9998 ? date : null;

    internal static string? Normalize(SourceField field, string value)
    {
        if (value.Length is 0 or > 200 || value.Any(char.IsControl)) return null;
        switch (field.Type)
        {
            case "texto": return value;
            case "opcao": return field.Options!.Contains(value) ? value : null;
            case "data": return ParseDate(value) is null ? null : value;
            case "hora": return TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? value : null;
            default:
                var normalized = value.Replace(',', '.');
                if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ||
                    number > 999999999999m || (field.Type == "valor" ? number < 0 : number <= 0)) return null;
                var precision = field.Type switch { "inteiro" => 0, "valor" => 2, _ => 6 };
                return decimal.Round(number, precision) == number ? number.ToString(CultureInfo.InvariantCulture) : null;
        }
    }

    internal static async Task RecordSaleAsync(IApplicationDbContext db, ITenantProvider tenant, Guid clinic, Guid patient,
        string kind, string name, int units, decimal price, CancellationToken ct)
    {
        var zone = AppHelpers.ResolveTimeZone(await db.Clinics.Where(x => x.Id == clinic).Select(x => x.Timezone).FirstAsync(ct));
        var fields = new Dictionary<string, string> { ["paciente"] = patient.ToString(), ["tipo"] = kind, ["tratamento"] = name,
            ["unidades"] = units.ToString(CultureInfo.InvariantCulture), ["receita"] = price.ToString(CultureInfo.InvariantCulture) };
        var seller = await db.Users.Where(x => x.Id == tenant.UserId && x.ClinicId == clinic).Select(x => x.Name).FirstOrDefaultAsync(ct);
        if (seller != null) fields["colaborador"] = seller;
        db.ReportSourceRecords.Add(new() { ClinicId = clinic, Source = "vendas", Reference = Guid.NewGuid().ToString(),
            Date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime), ValuesJson = JsonSerializer.Serialize(fields) });
    }
}
