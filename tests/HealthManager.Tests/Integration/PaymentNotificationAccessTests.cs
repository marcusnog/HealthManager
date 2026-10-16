using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using HealthManager.Api.Hubs;
using HealthManager.Domain;
using HealthManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthManager.Tests.Integration;

public sealed class PaymentNotificationAccessTests
{
    [Fact]
    public async Task Webhook_delivers_only_to_current_authorized_users_in_its_clinic()
    {
        var delivery = new RecordingLifetimeManager();
        await using var factory = new ApiTestFactory();
        await using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<HubLifetimeManager<PaymentHub>>(delivery)));
        using var client = app.CreateClient();
        var clinicId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var allowed = new User { ClinicId = clinicId, Email = "allowed@test.local", ScreenAccessJson = "[\"financeiro\"]" };
        var blocked = new User { ClinicId = clinicId, Email = "blocked@test.local", ScreenAccessJson = "[]" };
        var inactive = new User { ClinicId = clinicId, Email = "inactive@test.local", IsActive = false };
        var deleted = new User { ClinicId = clinicId, Email = "deleted@test.local", DeletedAt = DateTimeOffset.UtcNow };
        var otherClinic = new User { ClinicId = Guid.NewGuid(), Email = "other@test.local" };
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.AddRange(allowed, blocked, inactive, deleted, otherClinic);
            db.ClinicPaymentGatewayConfigs.Add(new ClinicPaymentGatewayConfig {
                ClinicId = clinicId, Provider = PaymentGatewayProvider.Asaas, WebhookSecret = "test-secret", IsEnabled = true });
            db.PaymentIntents.Add(new PaymentIntent {
                ClinicId = clinicId, ReceivableId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                Amount = 10, GatewayReference = "access-test", IdempotencyKey = "access-test" });
            await db.SaveChangesAsync();
        }
        async Task SendWebhook()
        {
            const string payload = """{"status":"CONFIRMED","payment":"access-test"}""";
            using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/payments/asaas") {
                Content = new StringContent(payload, Encoding.UTF8, "application/json") };
            request.Headers.Add("X-Clinic-Id", clinicId.ToString());
            request.Headers.Add("X-Hub-Signature-256", Convert.ToHexStringLower(HMACSHA256.HashData(
                Encoding.UTF8.GetBytes("test-secret"), Encoding.UTF8.GetBytes(payload))));
            (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        await SendWebhook();
        delivery.Recipients.Should().Contain(allowed.Id.ToString());
        delivery.Recipients.Should().NotContain(new[] { blocked.Id, inactive.Id, deleted.Id, otherClinic.Id }.Select(id => id.ToString()));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Users.FindAsync(allowed.Id))!.ScreenAccessJson = "[]";
            await db.SaveChangesAsync();
        }
        await SendWebhook();
        delivery.Recipients.Should().NotContain(allowed.Id.ToString());
    }

    private sealed class RecordingLifetimeManager() : DefaultHubLifetimeManager<PaymentHub>(NullLogger<DefaultHubLifetimeManager<PaymentHub>>.Instance)
    {
        public IReadOnlyList<string> Recipients { get; private set; } = [];
        public override Task SendUsersAsync(IReadOnlyList<string> userIds, string methodName, object?[] args, CancellationToken cancellationToken = default)
        {
            methodName.Should().Be("PaymentStatusChanged");
            Recipients = userIds;
            return Task.CompletedTask;
        }
    }
}
