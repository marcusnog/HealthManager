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
    private readonly Dictionary<string, byte[]> _store = [];

    public string BuildPatientDocumentPath(Guid clinicId, Guid patientId, string fileName) =>
        $"clinics/{clinicId}/patients/{patientId}/{fileName}";

    public async Task UploadPatientDocumentAsync(string storagePath, Stream content, string contentType, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, cancellationToken);
        _store[storagePath] = ms.ToArray();
    }

    public Task<Stream> DownloadPatientDocumentAsync(string storagePath, CancellationToken cancellationToken)
    {
        if (!_store.TryGetValue(storagePath, out var data))
            throw new KeyNotFoundException($"Arquivo nao encontrado no storage fake: {storagePath}");
        return Task.FromResult<Stream>(new MemoryStream(data));
    }
}
