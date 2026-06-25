using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Application;

public interface IApplicationDbContext
{
    DbSet<Clinic> Clinics { get; }
    DbSet<User> Users { get; }
    DbSet<Patient> Patients { get; }
    DbSet<Doctor> Doctors { get; }
    DbSet<Appointment> Appointments { get; }
    DbSet<Receivable> Receivables { get; }
    DbSet<Payment> Payments { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<WhatsAppMessage> WhatsAppMessages { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<OutboxEvent> OutboxEvents { get; }
    DbSet<PatientDocument> PatientDocuments { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<WebhookEvent> WebhookEvents { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ITenantProvider
{
    Guid? ClinicId { get; }
    Guid? UserId { get; }
    UserRole? Role { get; }
    bool IsPlatformAdmin { get; }
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IJwtTokenService
{
    AuthTokenBundle Generate(User user);
    string GeneratePatientToken(Patient patient);
}

public interface IPasswordHasher
{
    string Hash(string plainText);
    bool Verify(string plainText, string passwordHash);
}

public interface IStorageService
{
    string BuildPatientDocumentPath(Guid clinicId, Guid patientId, string fileName);
    Task UploadPatientDocumentAsync(string storagePath, Stream content, string contentType, CancellationToken cancellationToken);
    Task<Stream> DownloadPatientDocumentAsync(string storagePath, CancellationToken cancellationToken);
}

public interface IOutboxService
{
    Task EnqueueAsync(Guid? clinicId, string eventType, object payload, CancellationToken cancellationToken);
}

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request, string? userAgent, CancellationToken cancellationToken);
    Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, string? userAgent, CancellationToken cancellationToken);
    Task LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken);
}

public interface IClinicProvisioningService
{
    Task<ClinicProvisioningResponse> CreateClinicAsync(CreateClinicRequest request, CancellationToken cancellationToken);
    Task<UserResponse> CreateClinicUserAsync(Guid clinicId, CreateClinicUserRequest request, CancellationToken cancellationToken);
}

public interface IPatientService
{
    Task<PagedResult<PatientResponse>> ListAsync(PatientQuery query, CancellationToken cancellationToken);
    Task<PatientResponse> CreateAsync(CreatePatientRequest request, CancellationToken cancellationToken);
    Task<PatientResponse> UpdateAsync(Guid patientId, UpdatePatientRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid patientId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PatientDocumentResponse>> ListDocumentsAsync(Guid patientId, CancellationToken cancellationToken);
    Task<PatientDocumentResponse> UploadDocumentAsync(Guid patientId, UploadPatientDocumentRequest request, CancellationToken cancellationToken);
    Task<DownloadPatientDocumentResult> DownloadDocumentAsync(Guid patientId, Guid documentId, CancellationToken cancellationToken);
    Task DeleteDocumentAsync(Guid patientId, Guid documentId, CancellationToken cancellationToken);
    Task<PatientDocumentResponse> AddDocumentAsync(Guid patientId, CreatePatientDocumentRequest request, CancellationToken cancellationToken);
}

public interface IDoctorService
{
    Task<PagedResult<DoctorResponse>> ListAsync(DoctorQuery query, CancellationToken cancellationToken);
    Task<DoctorResponse> CreateAsync(CreateDoctorRequest request, CancellationToken cancellationToken);
    Task<DoctorResponse> UpdateAsync(Guid doctorId, UpdateDoctorRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid doctorId, CancellationToken cancellationToken);
}

public interface IAppointmentService
{
    Task<PagedResult<AppointmentResponse>> ListAsync(AppointmentQuery query, CancellationToken cancellationToken);
    Task<AppointmentResponse> CreateAsync(CreateAppointmentRequest request, CancellationToken cancellationToken);
    Task<AppointmentResponse> UpdateAsync(Guid appointmentId, UpdateAppointmentRequest request, CancellationToken cancellationToken);
    Task<AppointmentResponse> ConfirmAsync(Guid appointmentId, CancellationToken cancellationToken);
    Task<AppointmentResponse> CancelAsync(Guid appointmentId, CancellationToken cancellationToken);
}

public interface IFinancialService
{
    Task<PagedResult<ReceivableResponse>> ListReceivablesAsync(FinancialQuery query, CancellationToken cancellationToken);
    Task<PagedResult<PaymentResponse>> ListPaymentsAsync(PaymentQuery query, CancellationToken cancellationToken);
    Task<PaymentResponse> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken);
}

public interface IDashboardService
{
    Task<DashboardSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken);
}

public interface IWhatsAppWebhookService
{
    Task ProcessAsync(WhatsAppWebhookRequest request, CancellationToken cancellationToken);
}

public interface IPatientPortalService
{
    Task<PatientPortalAuthResponse> LoginAsync(PatientPortalLoginRequest request, CancellationToken cancellationToken);
    Task<PatientPortalProfileResponse> GetProfileAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<PatientPortalAppointmentResponse>> GetAppointmentsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<PatientPortalReceivableResponse>> GetReceivablesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<PatientDocumentResponse>> GetDocumentsAsync(CancellationToken cancellationToken);
    Task<Guid> RegenerateAccessTokenAsync(Guid patientId, CancellationToken cancellationToken);
}
