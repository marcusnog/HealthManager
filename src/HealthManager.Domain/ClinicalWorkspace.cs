namespace HealthManager.Domain;

public sealed class ClinicalCatalogItem : TenantEntity
{
    public string Kind { get; set; } = "Document";
    public string Folder { get; set; } = "Geral";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string FieldsJson { get; set; } = "[]";
    public Guid? OwnerUserId { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
}

public sealed class ClinicalEntry : TenantEntity
{
    public Guid AppointmentId { get; set; }
    public Guid PatientId { get; set; }
    public Guid DoctorId { get; set; }
    public Guid AuthorId { get; set; }
    public Guid ClientRequestId { get; set; }
    public Guid? CorrectsEntryId { get; set; }
    public string Kind { get; set; } = "Document";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string FieldsJson { get; set; } = "[]";
    public string ValuesJson { get; set; } = "{}";
    public string SignatureMode { get; set; } = "Paper";
    public string PatientName { get; set; } = "";
    public string DoctorName { get; set; } = "";
    public string DoctorCrm { get; set; } = "";
    public string ClinicName { get; set; } = "";
}
