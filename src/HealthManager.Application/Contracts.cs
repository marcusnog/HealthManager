using System.ComponentModel.DataAnnotations;
using HealthManager.Domain;

namespace HealthManager.Application;

public sealed record LoginRequest([Required][EmailAddress] string Email, [Required][MinLength(8)] string Password);
public sealed record RefreshTokenRequest([Required] string RefreshToken);
public sealed record AuthTokenBundle(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);
public sealed record UserResponse(Guid Id, Guid? ClinicId, string Name, string Email, UserRole Role);
public sealed record AuthResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, UserResponse User);

public sealed record CreateClinicRequest(
    [Required] string Name,
    [Required][RegularExpression("^[a-z0-9-]+$")] string Slug,
    [Required] string Timezone,
    [Required] string BusinessHoursJson,
    string? Cnpj,
    string? Email,
    string? Phone,
    string? Address,
    [Required] string AdminName,
    [Required][EmailAddress] string AdminEmail,
    [Required][MinLength(8)] string AdminPassword);

public sealed record CreateClinicUserRequest([Required] string Name, [Required][EmailAddress] string Email, [Required][MinLength(8)] string Password, [Required] UserRole Role);
public sealed record ClinicProvisioningResponse(Guid ClinicId, Guid AdminUserId);

public sealed record PatientQuery(int Page = 1, int PageSize = 20, string? Search = null, string? SortBy = null, string? SortDirection = null, string? Email = null, string? HealthInsurance = null);
public sealed record AppointmentQuery(int Page = 1, int PageSize = 20, DateOnly? Date = null, Guid? DoctorId = null, string? Status = null);
public sealed record FinancialQuery(int Page = 1, int PageSize = 20, string? Status = null, DateOnly? DateFrom = null, DateOnly? DateTo = null);
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public sealed record CreatePatientRequest(
    [Required][StringLength(160)] string Name,
    [Required][StringLength(14)] string Cpf,
    DateOnly? BirthDate,
    [Required][StringLength(20)] string Phone,
    string? Email,
    string? HealthInsurance,
    string? Notes);

public sealed record UpdatePatientRequest(
    [Required] string Name,
    [Required] string Phone,
    string? Email,
    string? HealthInsurance,
    string? Notes);

public sealed record CreatePatientDocumentRequest(
    [Required] string FileName,
    [Required] string ContentType,
    [Range(1, 10 * 1024 * 1024)] long SizeInBytes,
    Stream? Content = null);

public sealed record DownloadPatientDocumentResult(Stream Content, string ContentType, string FileName);
public sealed record PatientDocumentResponse(Guid Id, string FileName, string ContentType, long SizeInBytes, string StoragePath);
public sealed record PatientResponse(Guid Id, string Name, string Cpf, DateOnly? BirthDate, string Phone, string? Email, string? HealthInsurance, string? Notes, Guid PatientAccessToken);

public sealed record DoctorQuery(int Page = 1, int PageSize = 20, string? Search = null, string? SortBy = null, string? SortDirection = null);
public sealed record CreateDoctorRequest([Required] string Name, [Required] string Specialty, [Required] string Crm, string? Phone, string? Email);
public sealed record UpdateDoctorRequest([Required] string Name, [Required] string Specialty, string? Phone, string? Email, bool IsActive);
public sealed record DoctorResponse(Guid Id, string Name, string Specialty, string Crm, string? Phone, string? Email, bool IsActive);

public sealed record CreateAppointmentRequest(
    [Required] Guid PatientId,
    [Required] Guid DoctorId,
    [Required] DateTimeOffset StartAt,
    [Range(15, 240)] int DurationMinutes,
    string? Notes,
    [Required] string Type,
    [Range(0, double.MaxValue)] decimal Amount);

public sealed record UpdateAppointmentRequest(
    Guid? DoctorId,
    DateTimeOffset? StartAt,
    int? DurationMinutes,
    string? Notes,
    string? Type,
    decimal? Amount);

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
    string? Notes,
    string? PatientName = null,
    string? PatientPhone = null,
    string? DoctorName = null,
    string? DoctorSpecialty = null);

public sealed record ReceivableResponse(
    Guid Id,
    Guid AppointmentId,
    decimal OriginalAmount,
    decimal ReceivedAmount,
    decimal OutstandingAmount,
    ReceivableStatus Status,
    DateTimeOffset DueDate);

public sealed record PaymentQuery(int Page = 1, int PageSize = 20, Guid? ReceivableId = null, DateOnly? DateFrom = null, DateOnly? DateTo = null);
public sealed record CreatePaymentRequest([Required] Guid ReceivableId, [Range(0.01, double.MaxValue)] decimal Amount, [Required] PaymentMethod PaymentMethod, DateTimeOffset? PaidAt, string? Notes);
public sealed record PaymentResponse(Guid Id, Guid ReceivableId, decimal Amount, PaymentMethod PaymentMethod, DateTimeOffset PaidAt, PaymentStatus Status);
public sealed record DashboardSummaryResponse(int AppointmentsToday, int ConfirmedToday, int CancelledToday, decimal MonthlyRevenue, double NoShowRate, double ConfirmationRate);

public sealed record WhatsAppWebhookRequest(Guid? ClinicId, Guid? AppointmentId, [Required] string Phone, [Required] string Message, string? ProviderMessageId);

public sealed record PatientPortalLoginRequest([Required] string Cpf, [Required] string AccessToken);
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
