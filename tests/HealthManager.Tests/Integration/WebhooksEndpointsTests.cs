using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests.Integration;

public sealed class WebhooksEndpointsTests
{
    private static readonly Guid ClinicId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string TestWebhookSecret = "test-webhook-secret";

    [Fact]
    public async Task InvalidProvider_ShouldReturnBadRequest()
    {
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/webhooks/payments/bogus", new { });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MissingClinicIdHeader_ShouldReturnBadRequest()
    {
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/webhooks/payments/asaas", new { status = "CONFIRMED" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task NoGatewayConfigured_ShouldReturnBadRequest()
    {
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/payments/asaas")
        {
            Content = JsonContent.Create(new { status = "CONFIRMED" }),
            Headers = { { "X-Clinic-Id", ClinicId.ToString() } }
        };

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ValidWebhook_ShouldProcessAndReturnOk()
    {
        await using var factory = new ApiTestFactory();

        await factory.WithDbContextAsync(async dbContext =>
        {
            dbContext.ClinicPaymentGatewayConfigs.Add(new ClinicPaymentGatewayConfig
            {
                ClinicId = ClinicId,
                Provider = PaymentGatewayProvider.Asaas,
                Environment = PaymentGatewayEnvironment.Sandbox,
                WebhookSecret = TestWebhookSecret,
                IsEnabled = true
            });
            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateClient();
        var payload = """{"status":"CONFIRMED","payment":"gw-001","paymentExternalReference":"idem-test-001"}""";
        var signature = ComputeHmac(payload);
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/payments/asaas")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            Headers =
            {
                { "X-Clinic-Id", ClinicId.ToString() },
                { "X-Hub-Signature-256", signature }
            }
        };

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task InvalidPayload_MissingRequiredFields_ShouldReturnBadRequest()
    {
        await using var factory = new ApiTestFactory();

        await factory.WithDbContextAsync(async dbContext =>
        {
            dbContext.ClinicPaymentGatewayConfigs.Add(new ClinicPaymentGatewayConfig
            {
                ClinicId = ClinicId,
                Provider = PaymentGatewayProvider.Asaas,
                Environment = PaymentGatewayEnvironment.Sandbox,
                WebhookSecret = TestWebhookSecret,
                IsEnabled = true
            });
            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateClient();
        var payload = """{"something":"unexpected"}""";
        var signature = ComputeHmac(payload);
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/payments/asaas")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            Headers = { { "X-Clinic-Id", ClinicId.ToString() }, { "X-Hub-Signature-256", signature } }
        };

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ValidWebhook_WithMatchingIntent_ShouldConfirmPayment()
    {
        await using var factory = new ApiTestFactory();

        var receivableId = Guid.NewGuid();
        var intentId = Guid.NewGuid();

        await factory.WithDbContextAsync(async dbContext =>
        {
            dbContext.ClinicPaymentGatewayConfigs.Add(new ClinicPaymentGatewayConfig
            {
                ClinicId = ClinicId,
                Provider = PaymentGatewayProvider.Asaas,
                Environment = PaymentGatewayEnvironment.Sandbox,
                WebhookSecret = TestWebhookSecret,
                IsEnabled = true
            });

            dbContext.Receivables.Add(new Receivable
            {
                Id = receivableId,
                ClinicId = ClinicId,
                OriginalAmount = 200,
                ReceivedAmount = 0,
                Status = ReceivableStatus.Pending,
                DueDate = DateTimeOffset.UtcNow,
                Description = "Teste"
            });

            dbContext.PaymentIntents.Add(new PaymentIntent
            {
                Id = intentId,
                ClinicId = ClinicId,
                ReceivableId = receivableId,
                Amount = 200,
                Status = PaymentIntentStatus.Created,
                GatewayReference = "gw-match-001"
            });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateClient();
        var payload = """{"status":"CONFIRMED","payment":"gw-match-001","paymentExternalReference":"idem-match-001"}""";
        var signature = ComputeHmac(payload);
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/payments/asaas")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            Headers = { { "X-Clinic-Id", ClinicId.ToString() }, { "X-Hub-Signature-256", signature } }
        };

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await factory.WithDbContextAsync(async dbContext =>
        {
            var intent = await dbContext.PaymentIntents.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == intentId);
            intent.Status.Should().Be(PaymentIntentStatus.Confirmed);
            intent.GatewayReference.Should().Be("gw-match-001");
            intent.ConfirmedAt.Should().NotBeNull();

            var receivable = await dbContext.Receivables.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == receivableId);
            receivable.Status.Should().Be(ReceivableStatus.Paid);
            receivable.ReceivedAmount.Should().Be(200);

            var payment = await dbContext.Payments.IgnoreQueryFilters()
                .SingleAsync(x => x.ReceivableId == receivableId);
            payment.Amount.Should().Be(200);
            payment.Status.Should().Be(PaymentStatus.Paid);
        });
    }

    private static string ComputeHmac(string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestWebhookSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash);
    }
}
