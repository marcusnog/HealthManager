using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
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

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

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

public sealed class StorageService(HttpClient httpClient, IConfiguration configuration) : IStorageService
{
    private readonly string bucket = configuration["SUPABASE_BUCKET"] ?? "patient-documents";
    private readonly string? supabaseUrl = configuration["SUPABASE_URL"];
    private readonly string? supabaseKey = configuration["SUPABASE_KEY"];
    private readonly string localStorageRoot = configuration["LOCAL_STORAGE_ROOT"] ?? Path.Combine(Path.GetTempPath(), "healthmanager-storage");

    public string BuildPatientDocumentPath(Guid clinicId, Guid patientId, string fileName)
    {
        var sanitized = fileName.Replace(" ", "-").ToLowerInvariant();
        return $"{bucket}/clinics/{clinicId}/patients/{patientId}/{sanitized}";
    }

    public async Task UploadPatientDocumentAsync(string storagePath, Stream content, string contentType, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(supabaseKey))
        {
            var localAbsolutePath = ResolveLocalPath(storagePath);
            Directory.CreateDirectory(Path.GetDirectoryName(localAbsolutePath)!);
            await using var fileStream = File.Create(localAbsolutePath);
            await content.CopyToAsync(fileStream, cancellationToken);
            return;
        }

        var objectKey = storagePath.StartsWith($"{bucket}/", StringComparison.OrdinalIgnoreCase)
            ? storagePath[(bucket.Length + 1)..]
            : storagePath;
        var encodedObjectKey = string.Join("/", objectKey.Split('/').Select(Uri.EscapeDataString));
        var requestUri = $"{supabaseUrl.TrimEnd('/')}/storage/v1/object/{bucket}/{encodedObjectKey}";

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", supabaseKey);
        request.Headers.Add("apikey", supabaseKey);
        request.Headers.Add("x-upsert", "false");

        var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        request.Content = streamContent;

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Falha ao enviar arquivo para o Supabase Storage: {(int)response.StatusCode} {error}");
        }
    }

    public async Task<Stream> DownloadPatientDocumentAsync(string storagePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(supabaseKey))
        {
            var localAbsolutePath = ResolveLocalPath(storagePath);
            if (!File.Exists(localAbsolutePath))
            {
                throw new KeyNotFoundException("Arquivo nao encontrado no storage local.");
            }

            return File.OpenRead(localAbsolutePath);
        }

        var objectKey = storagePath.StartsWith($"{bucket}/", StringComparison.OrdinalIgnoreCase)
            ? storagePath[(bucket.Length + 1)..]
            : storagePath;
        var encodedObjectKey = string.Join("/", objectKey.Split('/').Select(Uri.EscapeDataString));
        var requestUri = $"{supabaseUrl.TrimEnd('/')}/storage/v1/object/authenticated/{bucket}/{encodedObjectKey}";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", supabaseKey);
        request.Headers.Add("apikey", supabaseKey);

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Falha ao baixar arquivo do Supabase Storage: {(int)response.StatusCode} {error}");
        }

        var memoryStream = new MemoryStream();
        await response.Content.CopyToAsync(memoryStream, cancellationToken);
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
            try
            {
                pendingEvent.Status = OutboxStatus.Processed;
                pendingEvent.ProcessedAt = DateTimeOffset.UtcNow;
                pendingEvent.Attempts += 1;
                pendingEvent.LastError = null;
            }
            catch (Exception ex)
            {
                pendingEvent.Status = OutboxStatus.Failed;
                pendingEvent.Attempts += 1;
                pendingEvent.LastError = ex.Message;
            }
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
