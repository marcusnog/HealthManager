namespace HealthManager.Domain;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}

public interface ITenantEntity
{
    Guid? ClinicId { get; set; }
}

public abstract class TenantEntity : Entity, ITenantEntity
{
    public Guid? ClinicId { get; set; }
}

public sealed class Clinic : Entity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Timezone { get; set; } = "America/Sao_Paulo";
    public string BusinessHoursJson { get; set; } = "{\"start\":\"08:00\",\"end\":\"18:00\"}";
    public string? Cnpj { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public int DefaultAppointmentMinutes { get; set; } = 30;
    public ClinicStatus Status { get; set; } = ClinicStatus.Active;
    public ICollection<User> Users { get; set; } = new List<User>();
    public ClinicWhatsAppConfig? WhatsAppConfig { get; set; }
    public ClinicPaymentGatewayConfig? PaymentGatewayConfig { get; set; }
    public ClinicNotificationConfig? NotificationConfig { get; set; }
    public ClinicBranding? Branding { get; set; }
}

public sealed class User : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Secretary;
    public bool IsActive { get; set; } = true;
    public Clinic? Clinic { get; set; }
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}

public sealed class Patient : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? SocialName { get; set; }
    public string Cpf { get; set; } = string.Empty;
    public string? Rg { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? Sex { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? SecondaryPhone { get; set; }
    public string? CommercialPhone { get; set; }
    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string? HealthInsurance { get; set; }
    public Guid? HealthInsuranceId { get; set; }
    public HealthInsurance? HealthInsuranceRef { get; set; }
    public string? MedicalRecordNumber { get; set; }
    public string? HealthInsuranceNumber { get; set; }
    public string? Cns { get; set; }
    public bool IsVip { get; set; }
    public bool ExcludeFromMarketing { get; set; }
    public bool ReceiveDirectMail { get; set; }
    public string? Tags { get; set; }
    public string? ZipCode { get; set; }
    public string? Street { get; set; }
    public string? Complement { get; set; }
    public string? Neighborhood { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Region { get; set; }
    public string? Company { get; set; }
    public string? Reference { get; set; }
    public string? AcquisitionSource { get; set; }
    public string? ReferredBy { get; set; }
    public string? MaritalStatus { get; set; }
    public string? Education { get; set; }
    public string? Profession { get; set; }
    public string? Religion { get; set; }
    public string? FatherName { get; set; }
    public string? MotherName { get; set; }
    public string? CompanionName { get; set; }
    public int? ChildrenCount { get; set; }
    public string? SpouseName { get; set; }
    public string? Notes { get; set; }
    public Guid PatientAccessToken { get; set; } = Guid.NewGuid();
    public ICollection<PatientDocument> Documents { get; set; } = new List<PatientDocument>();
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
}

public sealed class Doctor : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string Crm { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal ClinicSharePercentage { get; set; } = 100m;
    public ICollection<DoctorSpecialty> DoctorSpecialties { get; set; } = new List<DoctorSpecialty>();
}

public sealed class Appointment : TenantEntity
{
    public Guid PatientId { get; set; }
    public Guid DoctorId { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Scheduled;
    public ConfirmationStatus ConfirmationStatus { get; set; } = ConfirmationStatus.Pending;
    public AppointmentSource Source { get; set; } = AppointmentSource.Internal;
    public Guid AppointmentTypeId { get; set; }
    public AppointmentType AppointmentType { get; set; } = null!;
    public string? Notes { get; set; }
    public decimal Amount { get; set; }
    public Patient? Patient { get; set; }
    public Doctor? Doctor { get; set; }
}

public sealed class Receivable : TenantEntity
{
    public Guid? AppointmentId { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal ReceivedAmount { get; set; }
    public ReceivableStatus Status { get; set; } = ReceivableStatus.Pending;
    public DateTimeOffset DueDate { get; set; } = DateTimeOffset.UtcNow;
    public string? Description { get; set; }
    public Guid? ProfessionalId { get; set; }
    public decimal ClinicSharePercentage { get; set; } = 100m;
    public Appointment? Appointment { get; set; }
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

public sealed class Payment : TenantEntity
{
    public Guid ReceivableId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Paid;
    public DateTimeOffset PaidAt { get; set; } = DateTimeOffset.UtcNow;
    public string? DestinationBank { get; set; }
    public FundsRecipient FundsRecipient { get; set; } = FundsRecipient.Clinic;
    public decimal ClinicRevenueAmount { get; set; }
    public decimal ProfessionalPayableAmount { get; set; }
    public DateTimeOffset? ProfessionalPaidAt { get; set; }
    public DateTimeOffset? OwnerSettledAt { get; set; }
    public string? Notes { get; set; }
    public Receivable? Receivable { get; set; }
}

public sealed class HealthInsurance : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? ContactName { get; set; }
}

public sealed class Specialty : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public ICollection<DoctorSpecialty> DoctorSpecialties { get; set; } = new List<DoctorSpecialty>();
}

public sealed class DoctorSpecialty : TenantEntity
{
    public Guid DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;
    public Guid SpecialtyId { get; set; }
    public Specialty Specialty { get; set; } = null!;
}

public sealed class DoctorAvailability : TenantEntity
{
    public Guid DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool IsAvailable { get; set; } = true;
}

public sealed class Expense : TenantEntity
{
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public Guid CategoryId { get; set; }
    public ExpenseCategory Category { get; set; } = null!;
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Pix;
    public DateTimeOffset PaidAt { get; set; } = DateTimeOffset.UtcNow;
    public ExpenseStatus Status { get; set; } = ExpenseStatus.Paid;
    public string? Notes { get; set; }
}

public sealed class AppointmentType : TenantEntity
{
    public string Name { get; set; } = string.Empty;
}

public sealed class ExpenseCategory : TenantEntity
{
    public string Name { get; set; } = string.Empty;
}

public sealed class PatientDocument : TenantEntity
{
    public Guid PatientId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeInBytes { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public Patient? Patient { get; set; }
}


public sealed class WhatsAppMessage : TenantEntity
{
    public Guid? PatientId { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public WhatsAppMessageStatus Status { get; set; } = WhatsAppMessageStatus.Pending;
    public MessageDirection Direction { get; set; } = MessageDirection.Outbound;
    public string? ProviderMessageId { get; set; }
}

public sealed class AuditLog : TenantEntity
{
    public Guid? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string PayloadJson { get; set; } = "{}";
}

public sealed class OutboxEvent : Entity
{
    public Guid? ClinicId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? LastError { get; set; }
}

public sealed class RefreshToken : TenantEntity
{
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? UserAgent { get; set; }
    public User? User { get; set; }
}

public sealed class WebhookEvent : TenantEntity
{
    public string Source { get; set; } = "whatsapp";
    public string PayloadJson { get; set; } = "{}";
    public bool Processed { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

public sealed class ClinicalRecord : TenantEntity
{
    public Guid AppointmentId { get; set; }
    public Guid PatientId { get; set; }
    public Guid DoctorId { get; set; }
    public ClinicalRecordStatus Status { get; set; } = ClinicalRecordStatus.Draft;
    public string? ChiefComplaint { get; set; }
    public string? History { get; set; }
    public string? PhysicalExam { get; set; }
    public string? Assessment { get; set; }
    public string? Plan { get; set; }
    public DateTimeOffset? FinalizedAt { get; set; }
    public Appointment? Appointment { get; set; }
    public Patient? Patient { get; set; }
    public Doctor? Doctor { get; set; }
    public ICollection<ClinicalRecordAddendum> Addendums { get; set; } = new List<ClinicalRecordAddendum>();
}

public sealed class ClinicalRecordAddendum : TenantEntity
{
    public Guid ClinicalRecordId { get; set; }
    public string Content { get; set; } = string.Empty;
    public Guid AuthorId { get; set; }
    public ClinicalRecord? ClinicalRecord { get; set; }
    public User? Author { get; set; }
}

public sealed class PaymentIntent : TenantEntity
{
    public Guid ReceivableId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Pix;
    public PaymentIntentStatus Status { get; set; } = PaymentIntentStatus.Created;
    public string? Gateway { get; set; }
    public string? GatewayReference { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? FailureReason { get; set; }
    public Receivable? Receivable { get; set; }
}

public sealed class ClinicWhatsAppConfig : Entity
{
    public Guid ClinicId { get; set; }
    public string PhoneNumberOfId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string? WABAId { get; set; }
    public bool IsEnabled { get; set; }
    public Clinic? Clinic { get; set; }
}

public sealed class ClinicPaymentGatewayConfig : Entity
{
    public Guid ClinicId { get; set; }
    public PaymentGatewayProvider Provider { get; set; }
    public string? ApiKey { get; set; }
    public string? Secret { get; set; }
    public PaymentGatewayEnvironment Environment { get; set; } = PaymentGatewayEnvironment.Sandbox;
    public string? WebhookSecret { get; set; }
    public bool IsEnabled { get; set; }
    public Clinic? Clinic { get; set; }
}

public sealed class ClinicNotificationConfig : Entity
{
    public Guid ClinicId { get; set; }
    public int ReminderHoursBefore { get; set; } = 24;
    public string ChannelsJson { get; set; } = "[\"whatsapp\"]";
    public string? QuietHoursStart { get; set; }
    public string? QuietHoursEnd { get; set; }
    public bool EnableAutoReminders { get; set; } = true;
    public Clinic? Clinic { get; set; }
}

public sealed class ClinicBranding : Entity
{
    public Guid ClinicId { get; set; }
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public string? SecondaryColor { get; set; }
    public string? CustomClinicName { get; set; }
    public Clinic? Clinic { get; set; }
}

public enum ClinicStatus
{
    Active = 1,
    Suspended = 2
}

public enum UserRole
{
    PlatformAdmin = 1,
    Admin = 2,
    Secretary = 3,
    Doctor = 4,
    Patient = 5
}

public enum AppointmentStatus
{
    Scheduled = 1,
    Confirmed = 2,
    Cancelled = 3,
    Completed = 4,
    NoShow = 5,
    InProgress = 6
}

public enum AppointmentSource
{
    Internal = 1,
    WhatsApp = 2
}

public enum ConfirmationStatus
{
    Pending = 1,
    Confirmed = 2,
    Declined = 3
}

public enum ReceivableStatus
{
    Pending = 1,
    Partial = 2,
    Paid = 3,
    Cancelled = 4
}

public enum PaymentMethod
{
    Cash = 1,
    Pix = 2,
    CreditCard = 3,
    DebitCard = 4,
    Insurance = 5
}

public enum PaymentStatus
{
    Paid = 1,
    Refunded = 2
}

public enum FundsRecipient
{
    Clinic = 1,
    Owner = 2
}

public enum MessageDirection
{
    Inbound = 1,
    Outbound = 2
}

public enum WhatsAppMessageStatus
{
    Pending = 1,
    Sent = 2,
    Delivered = 3,
    Failed = 4
}

public enum OutboxStatus
{
    Pending = 1,
    Processed = 2,
    Failed = 3
}

public enum ExpenseStatus
{
    Paid = 1,
    Pending = 2,
    Cancelled = 3
}

public enum ClinicalRecordStatus
{
    Draft = 1,
    Finalized = 2
}

public enum PaymentIntentStatus
{
    Created = 1,
    Processing = 2,
    Confirmed = 3,
    Failed = 4,
    Cancelled = 5
}

public enum PaymentGatewayProvider
{
    Asaas = 1,
    MercadoPago = 2,
    Stripe = 3
}

public enum PaymentGatewayEnvironment
{
    Sandbox = 1,
    Production = 2
}
