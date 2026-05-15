using HealthManager.Application;
using HealthManager.Domain;

namespace HealthManager.Tests;

internal sealed class FakeTenantProvider : ITenantProvider
{
    public FakeTenantProvider(Guid? clinicId, UserRole? role = UserRole.Admin)
    {
        ClinicId = clinicId;
        Role = role;
        IsPlatformAdmin = role == UserRole.PlatformAdmin;
    }

    public Guid? ClinicId { get; }
    public Guid? UserId { get; } = Guid.NewGuid();
    public UserRole? Role { get; }
    public bool IsPlatformAdmin { get; }
}

internal sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; } = now;
}

internal sealed class FakeStorageService : IStorageService
{
    public string BuildPatientDocumentPath(Guid clinicId, Guid patientId, string fileName) =>
        $"bucket/clinics/{clinicId}/patients/{patientId}/{fileName}";

    public Task UploadPatientDocumentAsync(string storagePath, Stream content, string contentType, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<Stream> DownloadPatientDocumentAsync(string storagePath, CancellationToken cancellationToken)
        => Task.FromResult<Stream>(new MemoryStream("fake-file"u8.ToArray()));
}
