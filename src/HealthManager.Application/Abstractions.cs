using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Application;

public interface ITenantProvider
{
    Guid? ClinicId { get; }
    Guid? UserId { get; }
    UserRole? Role { get; }
    bool IsPlatformAdmin { get; }
}

public interface IStorageService
{
    string BuildPatientDocumentPath(Guid clinicId, Guid patientId, string fileName);
    Task UploadPatientDocumentAsync(string storagePath, Stream content, string contentType, CancellationToken cancellationToken);
    Task<Stream> DownloadPatientDocumentAsync(string storagePath, CancellationToken cancellationToken);
}

public interface IPasswordHasher
{
    string Hash(string plainText);
    bool Verify(string plainText, string passwordHash);
}

public interface IJwtTokenService
{
    AuthTokenBundle Generate(User user);
    string GeneratePatientToken(Patient patient);
}

public interface IOutboxService
{
    Task EnqueueAsync(Guid? clinicId, string eventType, object payload, CancellationToken cancellationToken);
}

public interface IApplicationDbContext
{
    DbSet<Clinic> Clinics { get; }
    DbSet<User> Users { get; }
    DbSet<Patient> Patients { get; }
    DbSet<Doctor> Doctors { get; }
    DbSet<Appointment> Appointments { get; }
    DbSet<Receivable> Receivables { get; }
    DbSet<Payment> Payments { get; }
    DbSet<WhatsAppMessage> WhatsAppMessages { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Expense> Expenses { get; }
    DbSet<OutboxEvent> OutboxEvents { get; }
    DbSet<PatientDocument> PatientDocuments { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<WebhookEvent> WebhookEvents { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
