using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using BCrypt.Net;
using HealthManager.Application;
using HealthManager.Domain;
using HealthManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HealthManager.Infrastructure;

public sealed class PasswordHasher : IPasswordHasher
{
    public string Hash(string plainText) => BCrypt.Net.BCrypt.HashPassword(plainText);

    public bool Verify(string plainText, string passwordHash) => BCrypt.Net.BCrypt.Verify(plainText, passwordHash);
}

public sealed class RequestTenantProvider(IHttpContextAccessor httpContextAccessor) : ITenantProvider
{
    public Guid? ClinicId => ParseGuid(FindClaim("clinic_id")) ?? ParseGuid(httpContextAccessor.HttpContext?.Request.Headers["X-Clinic-Id"].FirstOrDefault());
    public Guid? UserId => ParseGuid(FindClaim(ClaimTypes.NameIdentifier)) ?? ParseGuid(FindClaim(JwtRegisteredClaimNames.Sub));
    public UserRole? Role => Enum.TryParse<UserRole>(FindClaim(ClaimTypes.Role), true, out var role) ? role : null;
    public bool IsPlatformAdmin => Role == UserRole.PlatformAdmin;

    private string? FindClaim(string claimType) => httpContextAccessor.HttpContext?.User.Claims.FirstOrDefault(x => x.Type == claimType)?.Value;

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var parsed) ? parsed : null;
}

public sealed class StorageService(IAmazonS3 s3, IConfiguration configuration) : IStorageService
{
    private readonly string? bucket = configuration["AWS_S3_BUCKET"];
    private readonly string localStorageRoot = configuration["LOCAL_STORAGE_ROOT"] ?? Path.Combine(Path.GetTempPath(), "healthmanager-storage");

    public string BuildPatientDocumentPath(Guid clinicId, Guid patientId, string fileName)
    {
        var sanitized = fileName.Replace(" ", "-").ToLowerInvariant();
        return $"clinics/{clinicId}/patients/{patientId}/{sanitized}";
    }

    public async Task UploadPatientDocumentAsync(string storagePath, Stream content, string contentType, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bucket))
        {
            var localPath = ResolveLocalPath(storagePath);
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            await using var fileStream = File.Create(localPath);
            await content.CopyToAsync(fileStream, cancellationToken);
            return;
        }

        var request = new PutObjectRequest
        {
            BucketName  = bucket,
            Key         = storagePath,
            InputStream = content,
            ContentType = contentType,
        };

        var response = await s3.PutObjectAsync(request, cancellationToken);
        if ((int)response.HttpStatusCode >= 300)
            throw new InvalidOperationException($"Falha ao enviar arquivo para o S3: {response.HttpStatusCode}");
    }

    public async Task<Stream> DownloadPatientDocumentAsync(string storagePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bucket))
        {
            var localPath = ResolveLocalPath(storagePath);
            if (!File.Exists(localPath))
                throw new KeyNotFoundException("Arquivo nao encontrado no storage local.");
            return File.OpenRead(localPath);
        }

        var response = await s3.GetObjectAsync(bucket, storagePath, cancellationToken);
        var memoryStream = new MemoryStream();
        await response.ResponseStream.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;
        return memoryStream;
    }

    private string ResolveLocalPath(string storagePath)
    {
        var normalizedPath = storagePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(localStorageRoot, normalizedPath);
    }
}

public sealed class OutboxService(IApplicationDbContext dbContext) : IOutboxService
{
    public Task EnqueueAsync(Guid? clinicId, string eventType, object payload, CancellationToken cancellationToken)
    {
        dbContext.OutboxEvents.Add(new OutboxEvent
        {
            ClinicId = clinicId,
            EventType = eventType,
            PayloadJson = JsonSerializer.Serialize(payload),
            Status = OutboxStatus.Pending
        });

        return Task.CompletedTask;
    }
}

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "healthmanager";
    public string Audience { get; set; } = "healthmanager-web";
    public string Secret { get; set; } = "change-me-super-secret-key-32-bytes";
    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 30;
}

public sealed class JwtTokenService(IOptions<JwtOptions> options) : IJwtTokenService
{
    public AuthTokenBundle Generate(User user)
    {
        var config = options.Value;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(config.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString())
        };

        if (user.ClinicId.HasValue)
        {
            claims.Add(new Claim("clinic_id", user.ClinicId.Value.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: config.Issuer,
            audience: config.Audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
        var refreshToken = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        return new AuthTokenBundle(accessToken, refreshToken, expiresAt);
    }

    public string GeneratePatientToken(Patient patient)
    {
        var config = options.Value;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTimeOffset.UtcNow.AddDays(7);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, patient.Id.ToString()),
            new(ClaimTypes.NameIdentifier, patient.Id.ToString()),
            new(ClaimTypes.Name, patient.Name),
            new(ClaimTypes.Role, UserRole.Patient.ToString())
        };

        if (patient.ClinicId.HasValue)
        {
            claims.Add(new Claim("clinic_id", patient.ClinicId.Value.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: config.Issuer,
            audience: config.Audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public sealed class OutboxProcessor(AppDbContext dbContext)
{
    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var pendingEvents = await dbContext.OutboxEvents
            .Where(x => x.Status == OutboxStatus.Pending)
            .OrderBy(x => x.CreatedAt)
            .Take(25)
            .ToListAsync(cancellationToken);

        foreach (var pendingEvent in pendingEvents)
        {
            pendingEvent.Status = OutboxStatus.Processed;
            pendingEvent.ProcessedAt = DateTimeOffset.UtcNow;
            pendingEvent.Attempts += 1;
            pendingEvent.LastError = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return pendingEvents.Count;
    }
}

public static class AppInitializationExtensions
{
    public static async Task<IServiceProvider> InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await AppDbInitializer.InitializeAsync(services, cancellationToken);
        return services;
    }
}
