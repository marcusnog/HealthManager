using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests.Integration;

public sealed class CheckoutEndpointsTests
{
    private static readonly Guid ClinicId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ReceivableId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private static async Task SeedGatewayConfig(ApiTestFactory factory)
    {
        await factory.WithDbContextAsync(async dbContext =>
        {
            if (await dbContext.ClinicPaymentGatewayConfigs.AnyAsync(x => x.ClinicId == ClinicId))
                return;

            dbContext.ClinicPaymentGatewayConfigs.Add(new ClinicPaymentGatewayConfig
            {
                ClinicId = ClinicId,
                Provider = PaymentGatewayProvider.Asaas,
                Environment = PaymentGatewayEnvironment.Sandbox,
                IsEnabled = true
            });
            await dbContext.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task StaffCheckout_ShouldCreatePixCharge()
    {
        await using var factory = new ApiTestFactory();
        await SeedGatewayConfig(factory);
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var response = await client.PostAsJsonAsync("/checkout", new
        {
            receivableId = ReceivableId,
            paymentMethod = "Pix"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var checkout = await response.Content.ReadFromJsonAsync<CheckoutHttpResponse>();
        checkout!.Status.Should().Be("Processing");
        checkout.PixQrCode.Should().NotBeNullOrEmpty();
        checkout.PixCopyPaste.Should().NotBeNullOrEmpty();
        checkout.Amount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task StaffCheckout_ShouldCreateCardCharge()
    {
        await using var factory = new ApiTestFactory();
        await SeedGatewayConfig(factory);
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var response = await client.PostAsJsonAsync("/checkout", new
        {
            receivableId = ReceivableId,
            paymentMethod = "CreditCard"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var checkout = await response.Content.ReadFromJsonAsync<CheckoutHttpResponse>();
        checkout!.Status.Should().Be("Processing");
        checkout.CheckoutUrl.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task StaffCheckout_ShouldRejectCancelledReceivable()
    {
        await using var factory = new ApiTestFactory();
        await SeedGatewayConfig(factory);

        var cancelledId = Guid.NewGuid();
        await factory.WithDbContextAsync(async dbContext =>
        {
            dbContext.Receivables.Add(new Receivable
            {
                Id = cancelledId,
                ClinicId = ClinicId,
                OriginalAmount = 100,
                Status = ReceivableStatus.Cancelled,
                DueDate = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();
        });

        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var response = await client.PostAsJsonAsync("/checkout", new
        {
            receivableId = cancelledId,
            paymentMethod = "Pix"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task StaffCheckout_ShouldRejectAmountExceedingOutstanding()
    {
        await using var factory = new ApiTestFactory();
        await SeedGatewayConfig(factory);
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var response = await client.PostAsJsonAsync("/checkout", new
        {
            receivableId = ReceivableId,
            paymentMethod = "Pix",
            amount = 999999m
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task StaffCheckout_ShouldRejectNoGatewayConfig()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var response = await client.PostAsJsonAsync("/checkout", new
        {
            receivableId = ReceivableId,
            paymentMethod = "Pix"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetCheckout_ShouldReturnExistingCheckout()
    {
        await using var factory = new ApiTestFactory();
        await SeedGatewayConfig(factory);
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var createResponse = await client.PostAsJsonAsync("/checkout", new
        {
            receivableId = ReceivableId,
            paymentMethod = "Pix"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<CheckoutHttpResponse>();

        var getResponse = await client.GetAsync($"/checkout/{created!.PaymentIntentId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var fetched = await getResponse.Content.ReadFromJsonAsync<CheckoutHttpResponse>();
        fetched!.PaymentIntentId.Should().Be(created.PaymentIntentId);
        fetched.Status.Should().Be("Processing");
    }

    private sealed record CheckoutHttpResponse(
        Guid PaymentIntentId,
        Guid ReceivableId,
        decimal Amount,
        string PaymentMethod,
        string Status,
        string? PixQrCode,
        string? PixCopyPaste,
        string? CheckoutUrl,
        DateTimeOffset? ExpiresAt);
}
