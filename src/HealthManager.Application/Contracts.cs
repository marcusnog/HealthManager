using HealthManager.Domain;

namespace HealthManager.Application;

public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshTokenRequest(string RefreshToken);
public sealed record AuthTokenBundle(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);
public sealed record UserResponse(Guid Id, Guid? ClinicId, string Name, string Email, UserRole Role);
public sealed record AuthResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, UserResponse User);

public sealed record CreateClinicRequest(
    string Name,
    string Slug,
    string Timezone,
    string BusinessHoursJson,
    string? Cnpj,
    string? Email,
    string? Phone,
    string? Address,
    string AdminName,
    string AdminEmail,
    string AdminPassword);

public sealed record CreateClinicUserRequest(string Name, string Email, string Password, UserRole Role);
public sealed record ClinicProvisioningResponse(Guid ClinicId, Guid AdminUserId);

public sealed record PatientQuery(int Page = 1, int PageSize = 20, string? Search = null);
public sealed record AppointmentQuery(int Page = 1, int PageSize = 20, DateOnly? Date = null, Guid? DoctorId = null, string? Status = null);
public sealed record FinancialQuery(int Page = 1, int PageSize = 20, string? Status = null, DateOnly? DateFrom = null, DateOnly? DateTo = null);
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public sealed record CreatePatientRequest(
    string Name,
    string Cpf,
    DateOnly? BirthDate,
    string Phone,
    string? Email,
    string? HealthInsurance,
    string? Notes);

public sealed record UpdatePatientRequest(
    string Name,
    string Phone,
    string? Email,
    string? HealthInsurance,
    string? Notes);

public sealed record CreatePatientDocumentRequest(string FileName, string ContentType, long SizeInBytes);
public sealed record UploadPatientDocumentRequest(string FileName, string ContentType, long SizeInBytes, Stream Content);
public sealed record DownloadPatientDocumentResult(Stream Content, string ContentType, string FileName);
public sealed record PatientDocumentResponse(Guid Id, string FileName, string ContentType, long SizeInBytes, string StoragePath);
public sealed record PatientResponse(Guid Id, string Name, string Cpf, string Phone, string? Email, string? HealthInsurance, string? Notes, Guid PatientAccessToken);

public sealed record CreateDoctorRequest(string Name, string Specialty, string Crm, string? Phone, string? Email);
public sealed record UpdateDoctorRequest(string Name, string Specialty, string? Phone, string? Email, bool IsActive);
public sealed record DoctorResponse(Guid Id, string Name, string Specialty, string Crm, string? Phone, string? Email, bool IsActive);

public sealed record CreateAppointmentRequest(
    Guid PatientId,
    Guid DoctorId,
    DateTimeOffset StartAt,
    int DurationMinutes,
    string? Notes,
    string Type,
    decimal Amount);

public sealed record AppointmentResponse(
    Guid Id,
    Guid PatientId,
    Guid DoctorId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    AppointmentStatus Status,
    ConfirmationStatus ConfirmationStatus,
    string Type,
    decimal Amount,
    string? Notes);

public sealed record ReceivableResponse(
    Guid Id,
    Guid AppointmentId,
    decimal OriginalAmount,
    decimal ReceivedAmount,
    decimal OutstandingAmount,
    ReceivableStatus Status,
    DateTimeOffset DueDate);

public sealed record CreatePaymentRequest(Guid ReceivableId, decimal Amount, PaymentMethod PaymentMethod, DateTimeOffset? PaidAt, string? Notes);
public sealed record PaymentResponse(Guid Id, Guid ReceivableId, decimal Amount, PaymentMethod PaymentMethod, DateTimeOffset PaidAt, PaymentStatus Status);
public sealed record DashboardSummaryResponse(int AppointmentsToday, int ConfirmedToday, int CancelledToday, decimal MonthlyRevenue, double NoShowRate, double ConfirmationRate);

public sealed record WhatsAppWebhookRequest(Guid? ClinicId, Guid? AppointmentId, string Phone, string Message, string? ProviderMessageId);

public sealed record PatientPortalLoginRequest(string Cpf, string AccessToken);
public sealed record PatientPortalAuthResponse(string AccessToken, DateTimeOffset ExpiresAt, PatientPortalProfileResponse Patient);
public sealed record PatientPortalProfileResponse(Guid Id, string Name, string Cpf, DateOnly? BirthDate, string Phone, string? Email, string? HealthInsurance);
public sealed record PatientPortalAppointmentResponse(
    Guid Id,
    string DoctorName,
    string DoctorSpecialty,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    AppointmentStatus Status,
    string Type,
    string? Notes,
    decimal Amount);
public sealed record PatientPortalReceivableResponse(
    Guid Id,
    decimal OriginalAmount,
    decimal ReceivedAmount,
    decimal OutstandingAmount,
    ReceivableStatus Status,
    DateTimeOffset DueDate);
