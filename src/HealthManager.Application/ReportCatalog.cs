using System.Globalization;
using System.Text.Json;

namespace HealthManager.Application;

public sealed record ReportDefinition(string Id, string Group, string Title, string Purpose, string Calculation, string? Source);
public sealed record SourceField(string Key, string Type, bool Required, string[]? Options);
public sealed record SourceDefinition(string Id, string Title, SourceField[] Fields)
{
    public string[] Header => ["referencia", "data", .. Fields.Select(f => f.Key)];
}
public sealed record ReportCatalog(ReportDefinition[] Reports, SourceDefinition[] Sources)
{
    public static readonly ReportCatalog Instance = Read();
    private static ReportCatalog Read()
    {
        using var stream = typeof(ReportCatalog).Assembly.GetManifestResourceStream("Reports.Catalog")!;
        return JsonSerializer.Deserialize<ReportCatalog>(stream, JsonSerializerOptions.Web)!;
    }
    public static SourceDefinition Source(string id) => Instance.Sources.FirstOrDefault(x => x.Id == id)
        ?? throw new KeyNotFoundException("Fonte desconhecida.");
}
public sealed record ReportDirectory(ReportDefinition[] Reports, SourceDefinition[] Sources, DateOnly Today);
public sealed record ReportCsv(string Csv);
public sealed record SourcePage(string Csv, bool HasMore);
public sealed record ReportMetric(string Label, decimal? Value, string Unit = "numero");
public sealed record ReportRow(string Label, IReadOnlyList<ReportMetric> Metrics);
public sealed record ReportResult(IReadOnlyList<ReportRow> Rows);

internal sealed record SourceLine(DateOnly Date, Dictionary<string, string> Fields, string Reference)
{
    public string Text(string key) => Fields.GetValueOrDefault(key, "");
    public DateOnly? Day(string key) => Fields.TryGetValue(key, out var s) ? DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    public decimal? Number(string key) => Fields.TryGetValue(key, out var s) ? decimal.Parse(s, CultureInfo.InvariantCulture) : null;
}

internal static class ReportDates
{
    public static void Validate(DateOnly from, DateOnly to)
    {
        if (from.Year < 1900 || to.Year > 9998 || to < from || to.DayNumber - from.DayNumber >= 366)
            throw new InvalidOperationException("Selecione um período de até 366 dias com datas válidas.");
    }
    public static DateTimeOffset Utc(DateOnly day, TimeOnly time, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(time);
        // Some historical Brazilian DST transitions skipped midnight.
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
    }
}
