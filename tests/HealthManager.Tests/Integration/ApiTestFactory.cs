using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HealthManager.Application;
using HealthManager.Domain;
using HealthManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HealthManager.Tests.Integration;

public sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    private readonly string databaseName = $"healthmanager-tests-{Guid.NewGuid()}";

    public sealed record AuthSession(string AccessToken, string RefreshToken, string Email, string Role);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["USE_INMEMORY_DATABASE"] = "true",
                ["INMEMORY_DATABASE_NAME"] = databaseName,
                ["SENTRY_DSN"] = ""
            });
        });
    }

    public async Task<AuthSession> LoginWithSessionAsync(string email, string password)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/auth/login", new
        {
            email,
            password
        });

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Login failed with status {(int)response.StatusCode}: {body}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        var payload = await JsonDocument.ParseAsync(stream);
        var root = payload.RootElement;
        return new AuthSession(
            root.GetProperty("accessToken").GetString()!,
            root.GetProperty("refreshToken").GetString()!,
            root.GetProperty("user").GetProperty("email").GetString()!,
            root.GetProperty("user").GetProperty("role").GetString()!);
    }

    public async Task<string> LoginAsync(string email, string password)
    {
        var session = await LoginWithSessionAsync(email, password);
        return session.AccessToken;
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync(string email, string password)
    {
        var token = await LoginAsync(email, password);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task SeedSecondClinicPatientAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var secondClinicId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var secondPatientId = Guid.Parse("99999999-9999-9999-9999-999999999999");

        if (await dbContext.Clinics.IgnoreQueryFilters().AnyAsync(x => x.Id == secondClinicId))
        {
            return;
        }

        dbContext.Clinics.Add(new Clinic
        {
            Id = secondClinicId,
            Name = "Clinica Nebula",
            Slug = "clinica-nebula",
            Timezone = "America/Sao_Paulo",
            BusinessHoursJson = "{\"start\":\"08:00\",\"end\":\"18:00\"}"
        });

        dbContext.Patients.Add(new Patient
        {
            Id = secondPatientId,
            ClinicId = secondClinicId,
            Name = "Paciente de Outro Tenant",
            Cpf = "99888777666",
            Phone = "11944443333"
        });

        await dbContext.SaveChangesAsync();
    }

    public async Task<T> WithDbContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await action(dbContext);
    }

    public async Task WithDbContextAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await action(dbContext);
    }
}
