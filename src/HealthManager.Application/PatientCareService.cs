using System.Globalization;
using System.Text.Json;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Application;

public sealed record CareInput(int Revision, DateOnly? Date, Dictionary<string, string> Fields, Dictionary<string, Guid> Links);
public sealed record CareItem(Guid Id, int Revision, DateOnly Date, Dictionary<string, string> Fields, Dictionary<string, Guid> Links);
public sealed record CareChoice(Guid Id, string Label, DateOnly? Date = null);
public sealed record CareLink(string Key, bool Required, List<CareChoice> Options);
public sealed record CareWorkspace(DateOnly Today, SourceField[] Fields, List<CareLink> Links, List<CareItem> Records);

public sealed class PatientCareService(IApplicationDbContext db, ITenantProvider tenant)
{
    private static readonly Dictionary<string, string[]> Inputs = new()
    {
        ["reservas"] = [], ["espera"] = ["saida"], ["horarios"] = ["chegada", "inicio"],
        ["indicacoes"] = ["tratamento", "contratacao"],
        ["programas"] = ["modalidade", "inicio", "vencimento", "renovacao", "abandono"],
        ["servicos"] = ["servico", "categoria", "quantidade", "previsto", "entrega", "receita", "custo"],
        ["parcelas"] = [], ["etapas"] = ["protocolo"], ["interrupcoes"] = ["protocolo"],
        ["insumos"] = ["insumo", "unidade", "quantidade"]
    };

    private Guid Clinic => TenantGuard.RequireClinicId(tenant);
    private async Task<TimeZoneInfo> Check(Guid patient, string source, CancellationToken ct)
    {
        if (!Inputs.ContainsKey(source)) throw new KeyNotFoundException("Tipo de acompanhamento desconhecido.");
        if (!await db.Patients.AnyAsync(p => p.ClinicId == Clinic && p.Id == patient, ct))
            throw new KeyNotFoundException("Paciente não encontrado.");
        return AppHelpers.ResolveTimeZone(await db.Clinics.Where(c => c.Id == Clinic).Select(c => c.Timezone).SingleAsync(ct));
    }

    private static SourceField[] Fields(string source) => Inputs[source].Select(key => {
        if (key == "tratamento") return new SourceField(key, "texto", true, null);
        var field = ReportCatalog.Source(source).Fields.Single(f => f.Key == key);
        return source == "horarios" && key == "inicio" ? field with { Required = false } : field;
    }).ToArray();

    private IQueryable<PatientCareRecord> Records(Guid patient, string source) =>
        db.PatientCareRecords.Where(x => x.ClinicId == Clinic && x.PatientId == patient && x.Source == source);

    internal static Dictionary<string, string> Details(PatientCareRecord row) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(row.FieldsJson)!;

    private static CareItem Item(PatientCareRecord row)
    {
        var links = new Dictionary<string, Guid>();
        void Add(string key, Guid? id) { if (id.HasValue) links[key] = id.Value; }
        Add("consulta", row.AppointmentId); Add("medico", row.DoctorId); Add("pagamento", row.PaymentId);
        Add("titulo", row.ReceivableId); Add("aplicacao", row.ApplicationId); Add("programa", row.ContractId);
        var fields = Details(row);
        fields.Remove("agendado");
        return new(row.Id, row.Revision, row.Date, fields, links);
    }

    public async Task<CareWorkspace> ReadAsync(Guid patient, string source, CancellationToken ct)
    {
        var zone = await Check(patient, source, ct);
        var rows = await Records(patient, source).AsNoTracking().OrderByDescending(x => x.Date).ThenBy(x => x.Id).ToListAsync(ct);
        return new(Local(DateTimeOffset.UtcNow, zone), Fields(source), await Choices(patient, source, zone, ct), rows.Select(Item).ToList());
    }

    internal static DateOnly Local(DateTimeOffset time, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, zone).DateTime);
    private async Task<List<CareLink>> Choices(Guid patient, string source, TimeZoneInfo zone, CancellationToken ct)
    {
        List<CareLink> links = [];
        string Time(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, zone).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        if (source is "horarios" or "indicacoes" or "servicos")
        {
            var appointments = await db.Appointments.Where(x => x.ClinicId == Clinic && x.PatientId == patient)
                .OrderByDescending(x => x.StartAt).Select(x => new { x.Id, x.StartAt, x.Doctor!.Name }).ToListAsync(ct);
            links.Add(new("consulta", source == "horarios", appointments.Select(x => new CareChoice(x.Id, $"{Time(x.StartAt)} · {x.Name}", Local(x.StartAt, zone))).ToList()));
        }
        if (source == "programas")
            links.Add(new("medico", true, await db.Doctors.Where(x => x.ClinicId == Clinic).OrderBy(x => x.Name)
                .Select(x => new CareChoice(x.Id, x.Name + " · " + x.Crm, null)).ToListAsync(ct)));
        if (source == "reservas")
        {
            var payments = await db.Payments.Where(x => x.ClinicId == Clinic && x.Status == PaymentStatus.Paid && x.Amount > 0 &&
                x.Receivable!.Appointment!.PatientId == patient).OrderByDescending(x => x.PaidAt)
                .Select(x => new { x.Id, x.PaidAt, x.Amount, x.Receivable!.Appointment!.StartAt }).ToListAsync(ct);
            links.Add(new("pagamento", true, payments.Select(x => new CareChoice(x.Id,
                $"{Time(x.PaidAt)} · R$ {x.Amount.ToString("F2", CultureInfo.GetCultureInfo("pt-BR"))} · consulta {Time(x.StartAt)}", Local(x.PaidAt, zone))).ToList()));
        }
        if (source is "servicos" or "parcelas" or "indicacoes")
        {
            var contracts = await Records(patient, "programas").OrderByDescending(x => x.Date).ToListAsync(ct);
            links.Add(new("programa", source != "indicacoes", contracts.Select(x => new CareChoice(x.Id, ContractLabel(x), x.Date)).ToList()));
        }
        if (source == "parcelas")
        {
            var receivables = await db.Receivables.Where(x => x.ClinicId == Clinic && x.Status != ReceivableStatus.Cancelled &&
                (x.AppointmentId == null ? x.PatientId == patient : x.Appointment!.PatientId == patient))
                .OrderBy(x => x.DueDate).Select(x => new { x.Id, x.DueDate, x.OriginalAmount, x.Description }).ToListAsync(ct);
            links.Add(new("titulo", true, receivables.Select(x => new CareChoice(x.Id,
                $"{x.Description} · {Local(x.DueDate, zone):dd/MM/yyyy} · R$ {x.OriginalAmount.ToString("F2", CultureInfo.GetCultureInfo("pt-BR"))}")).ToList()));
        }
        if (source is "etapas" or "insumos")
        {
            var applications = await db.PatientApplications.Where(x => x.ClinicId == Clinic && x.PatientId == patient)
                .OrderByDescending(x => x.AppliedAt).Select(x => new { x.Id, x.AppliedAt, x.Product!.Name, x.Quantity }).ToListAsync(ct);
            links.Add(new("aplicacao", source == "insumos", applications.Select(x => new CareChoice(x.Id,
                $"{Time(x.AppliedAt)} · {x.Name} ({x.Quantity})", Local(x.AppliedAt, zone))).ToList()));
        }
        return links;
    }

    internal static string ContractLabel(PatientCareRecord row) => $"{Details(row)["modalidade"]} · {row.Date:dd/MM/yyyy} · {row.Id:N}";

    public async Task<CareItem> SaveAsync(Guid patient, string source, Guid id, CareInput input, CancellationToken ct)
    {
        var zone = await Check(patient, source, ct);
        var row = await Records(patient, source).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (id == Guid.Empty || input.Fields == null || input.Links == null) throw new InvalidOperationException("Registro inválido.");
        if ((row?.Revision ?? 0) != input.Revision) throw new DbUpdateConcurrencyException("Recarregue o registro antes de editar.");
        var choices = await Choices(patient, source, zone, ct);
        var selected = new Dictionary<string, CareChoice>();
        if (input.Links.Keys.Except(choices.Select(x => x.Key)).Any()) throw new InvalidOperationException("Vínculo desconhecido.");
        foreach (var link in choices)
        {
            if (input.Links.TryGetValue(link.Key, out var key))
                selected[link.Key] = link.Options.Find(x => x.Id == key) ?? throw new InvalidOperationException("Vínculo indisponível para este paciente.");
            else if (link.Required) throw new InvalidOperationException("Selecione o vínculo obrigatório.");
        }
        Guid? Link(string key) => selected.GetValueOrDefault(key)?.Id;
        var date = source switch {
            "horarios" => selected["consulta"].Date, "reservas" => selected["pagamento"].Date,
            "insumos" => selected["aplicacao"].Date, "parcelas" => selected["programa"].Date, _ => input.Date
        };
        if (!date.HasValue || date.Value.Year is < 1900 or > 9998) throw new InvalidOperationException("Informe uma data válida.");
        var schema = Fields(source);
        if (input.Fields.Keys.Except(schema.Select(f => f.Key)).Any()) throw new InvalidOperationException("Campo desconhecido.");
        var values = new Dictionary<string, string>();
        foreach (var field in schema)
        {
            var value = input.Fields.GetValueOrDefault(field.Key)?.Trim();
            if (string.IsNullOrEmpty(value) && !field.Required) continue;
            values[field.Key] = ReportSourcesService.Normalize(field, value ?? "") ?? throw new InvalidOperationException($"Confira o campo {field.Key}.");
        }
        var line = new SourceLine(date.Value, values, id.ToString());
        if (source == "servicos" && selected.TryGetValue("consulta", out var visit) &&
            (line.Text("categoria") != "Consulta" || line.Number("quantidade") != 1 || line.Day("entrega") != null || visit.Date < date))
            throw new InvalidOperationException("Vincule uma consulta por unidade da categoria Consulta. A agenda informa a entrega.");
        if (source == "indicacoes" && selected.TryGetValue("programa", out var sale) &&
            (sale.Date < date || line.Day("contratacao") != null))
            throw new InvalidOperationException("O contrato fornece a data de contratação e deve ser posterior ou simultâneo à indicação.");
        if (schema.Any(f => f.Type == "data" && line.Day(f.Key) < date) ||
            (source == "horarios" && values.ContainsKey("inicio") && string.CompareOrdinal(line.Text("inicio"), line.Text("chegada")) < 0) ||
            (source == "programas" && (line.Day("inicio") > line.Day("vencimento") || line.Day("abandono") < line.Day("inicio") || line.Day("abandono") > line.Day("vencimento"))) ||
            (source == "servicos" && date < selected["programa"].Date))
            throw new InvalidOperationException("Confira a sequência de datas e horários.");
        // The database also enforces uniqueness for simultaneous requests.
        var other = Records(patient, source).Where(x => x.Id != id);
        var singleLink = source switch {
            "horarios" or "servicos" when input.Links.ContainsKey("consulta") => other.Where(x => x.AppointmentId == input.Links["consulta"]),
            "reservas" => other.Where(x => x.PaymentId == input.Links["pagamento"]),
            "parcelas" => other.Where(x => x.ReceivableId == input.Links["titulo"]),
            "etapas" when input.Links.ContainsKey("aplicacao") => other.Where(x => x.ApplicationId == input.Links["aplicacao"]),
            _ => null
        };
        if (singleLink != null && await singleLink.AnyAsync(ct)) throw new DbUpdateConcurrencyException("Este vínculo já está em uso.");
        if (row is null)
        {
            row = new() { Id = id, ClinicId = Clinic, PatientId = patient, Source = source };
            db.PatientCareRecords.Add(row);
        }
        if (source == "horarios")
        {
            var previous = Details(row);
            if (row.AppointmentId == Link("consulta") && previous.TryGetValue("agendado", out var original))
            {
                values["agendado"] = original;
                date = row.Date;
            }
            else
            {
                var appointmentId = Link("consulta");
                var scheduled = await db.Appointments.Where(x => x.ClinicId == Clinic && x.PatientId == patient && x.Id == appointmentId)
                    .Select(x => x.StartAt).SingleAsync(ct);
                var local = TimeZoneInfo.ConvertTime(scheduled, zone);
                values["agendado"] = local.ToString("HH:mm", CultureInfo.InvariantCulture);
                date = DateOnly.FromDateTime(local.DateTime);
            }
        }
        row.AppointmentId = Link("consulta"); row.DoctorId = Link("medico"); row.PaymentId = Link("pagamento");
        row.ReceivableId = Link("titulo"); row.ContractId = Link("programa"); row.ApplicationId = Link("aplicacao");
        row.Date = date.Value;
        row.FieldsJson = JsonSerializer.Serialize(values);
        row.Revision++;
        await db.SaveChangesAsync(ct);
        return Item(row);
    }
}
