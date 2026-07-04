using Amazon.S3;
using HealthManager.Application;
using HealthManager.Domain;
using HealthManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace HealthManager.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var jwtSecret = configuration["JWT_SECRET"];
        if (environment.IsProduction() && string.IsNullOrWhiteSpace(jwtSecret))
            throw new InvalidOperationException("JWT_SECRET must be set in production.");

        services.Configure<JwtOptions>(options =>
        {
            options.Issuer = configuration["JWT_ISSUER"] ?? "healthmanager";
            options.Audience = configuration["JWT_AUDIENCE"] ?? "healthmanager-web";
            options.Secret = jwtSecret ?? "change-me-super-secret-key-32-bytes";
            options.AccessTokenMinutes = int.TryParse(configuration["JWT_ACCESS_TOKEN_MINUTES"], out var accessMinutes) ? accessMinutes : 30;
            options.RefreshTokenDays = int.TryParse(configuration["JWT_REFRESH_TOKEN_DAYS"], out var refreshDays) ? refreshDays : 30;
        });

        services.AddHttpContextAccessor();
        services.AddScoped<ITenantProvider, RequestTenantProvider>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        if (!string.IsNullOrWhiteSpace(configuration["AWS_S3_BUCKET"]))
            services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());
        services.AddScoped<IStorageService, StorageService>();
        services.AddScoped<IOutboxService, OutboxService>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<OutboxProcessor>();

        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            var useInMemoryDatabase = bool.TryParse(configuration["USE_INMEMORY_DATABASE"], out var parsed) && parsed;

            if (useInMemoryDatabase)
            {
                var databaseName = configuration["INMEMORY_DATABASE_NAME"] ?? $"healthmanager-{Guid.NewGuid()}";
                options.UseInMemoryDatabase(databaseName);
                return;
            }

            var connectionString = configuration["DATABASE_URL"]
                                   ?? configuration.GetConnectionString("DefaultConnection")
                                   ?? "Host=localhost;Port=5432;Database=healthmanager;Username=postgres;Password=postgres";

            options.UseNpgsql(connectionString);
        });

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<AppDbContext>());

        var jwtSettings = new JwtOptions
        {
            Issuer = configuration["JWT_ISSUER"] ?? "healthmanager",
            Audience = configuration["JWT_AUDIENCE"] ?? "healthmanager-web",
            Secret = jwtSecret ?? "change-me-super-secret-key-32-bytes"
        };

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidAudience = jwtSettings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret))
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("PlatformAdminOnly", policy => policy.RequireRole(UserRole.PlatformAdmin.ToString()));
            options.AddPolicy("ClinicAdminOrSecretary", policy => policy.RequireRole(UserRole.Admin.ToString(), UserRole.Secretary.ToString()));
            options.AddPolicy("ClinicStaff", policy => policy.RequireRole(UserRole.Admin.ToString(), UserRole.Secretary.ToString(), UserRole.Doctor.ToString()));
            options.AddPolicy("PatientPortal", policy => policy.RequireRole(UserRole.Patient.ToString()));
        });

        return services;
    }
}
