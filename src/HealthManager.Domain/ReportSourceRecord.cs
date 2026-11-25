namespace HealthManager.Domain;

public sealed class ReportSourceRecord : TenantEntity
{
    public required string Source { get; set; }
    public required string Reference { get; set; }
    public DateOnly Date { get; set; }
    public required string ValuesJson { get; set; }
}
