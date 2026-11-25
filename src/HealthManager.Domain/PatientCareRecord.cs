namespace HealthManager.Domain;

public sealed class PatientCareRecord : TenantEntity
{
    public Guid PatientId { get; set; }
    public required string Source { get; set; }
    public DateOnly Date { get; set; }
    public Guid? AppointmentId { get; set; }
    public Guid? DoctorId { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? ReceivableId { get; set; }
    public Guid? ApplicationId { get; set; }
    public Guid? ContractId { get; set; }
    public string FieldsJson { get; set; } = "{}";
    public int Revision { get; set; }
    public Patient? Patient { get; set; }
    public Doctor? Doctor { get; set; }
    public Appointment? Appointment { get; set; }
    public Payment? Payment { get; set; }
    public Receivable? Receivable { get; set; }
    public PatientApplication? Application { get; set; }
    public PatientCareRecord? Contract { get; set; }
}
