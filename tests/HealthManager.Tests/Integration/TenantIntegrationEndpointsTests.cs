using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace HealthManager.Tests.Integration;

public sealed class TenantIntegrationEndpointsTests
{
    [Fact]
    public async Task Get_ShouldReturnEmptyConfig_ForNewClinic()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var response = await client.GetAsync("/tenant/integration");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<TenantIntegrationResult>();
        body!.DefaultAppointmentMinutes.Should().Be(30);
        body.WhatsApp.Should().BeNull();
        body.PaymentGateway.Should().BeNull();
        body.Notification.Should().BeNull();
        body.Branding.Should().BeNull();
    }

    [Fact]
    public async Task PutWhatsApp_ShouldUpsertConfig()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var response = await client.PutAsJsonAsync("/tenant/integration/whatsapp", new
        {
            phoneNumberOfId = "1234567890",
            accessToken = "test-token-abc",
            wABAId = "waba-123",
            isEnabled = true
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<WhatsAppConfigResult>();
        body!.PhoneNumberOfId.Should().Be("1234567890");
        body.WABAId.Should().Be("waba-123");
        body.IsEnabled.Should().BeTrue();
        body.Id.Should().NotBeEmpty();

        // Upsert same config again
        var upsert = await client.PutAsJsonAsync("/tenant/integration/whatsapp", new
        {
            phoneNumberOfId = "1234567890",
            accessToken = "test-token-updated",
            wABAId = (string?)null,
            isEnabled = false
        });
        upsert.StatusCode.Should().Be(HttpStatusCode.OK);
        var upserted = await upsert.Content.ReadFromJsonAsync<WhatsAppConfigResult>();
        upserted!.Id.Should().Be(body.Id);
        upserted.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task PutPaymentGateway_ShouldUpsertConfig()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var response = await client.PutAsJsonAsync("/tenant/integration/payment-gateway", new
        {
            provider = "Asaas",
            apiKey = "ak_test",
            secret = "sk_test",
            environment = "Sandbox",
            webhookSecret = "whsec_test",
            isEnabled = true
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PaymentGatewayConfigResult>();
        body!.Provider.Should().Be("Asaas");
        body.Environment.Should().Be("Sandbox");
        body.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task PutNotification_ShouldUpsertConfig()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var response = await client.PutAsJsonAsync("/tenant/integration/notification", new
        {
            reminderHoursBefore = 48,
            channelsJson = "[\"whatsapp\",\"sms\"]",
            quietHoursStart = "22:00",
            quietHoursEnd = "07:00",
            enableAutoReminders = false
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<NotificationConfigResult>();
        body!.ReminderHoursBefore.Should().Be(48);
        body.ChannelsJson.Should().Be("[\"whatsapp\",\"sms\"]");
        body.EnableAutoReminders.Should().BeFalse();
    }

    [Fact]
    public async Task PutBranding_ShouldUpsertConfig()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var response = await client.PutAsJsonAsync("/tenant/integration/branding", new
        {
            logoUrl = "https://example.com/logo.png",
            primaryColor = "#FF5733",
            secondaryColor = "#33FF57",
            customClinicName = "Aurora Premium"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<BrandingResult>();
        body!.LogoUrl.Should().Be("https://example.com/logo.png");
        body.PrimaryColor.Should().Be("#FF5733");
        body.CustomClinicName.Should().Be("Aurora Premium");
    }

    [Fact]
    public async Task Get_ShouldReturnAllConfigs_AfterUpserts()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        await client.PutAsJsonAsync("/tenant/integration/whatsapp", new { phoneNumberOfId = "111", accessToken = "tok", isEnabled = true });
        await client.PutAsJsonAsync("/tenant/integration/payment-gateway", new { provider = "Stripe", apiKey = "sk", secret = "ss", environment = "Production", webhookSecret = "wh", isEnabled = true });
        await client.PutAsJsonAsync("/tenant/integration/notification", new { reminderHoursBefore = 24, channelsJson = "[\"whatsapp\"]", enableAutoReminders = true });
        await client.PutAsJsonAsync("/tenant/integration/branding", new { logoUrl = "https://x.com/logo.png", primaryColor = "#000", secondaryColor = "#FFF", customClinicName = "Test" });

        var response = await client.GetAsync("/tenant/integration");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<TenantIntegrationResult>();
        body!.WhatsApp.Should().NotBeNull();
        body.PaymentGateway.Should().NotBeNull();
        body.Notification.Should().NotBeNull();
        body.Branding.Should().NotBeNull();
    }

    [Fact]
    public async Task Secretary_ShouldNotAccessIntegration()
    {
        await using var factory = new ApiTestFactory();
        // Secretary role seed - need to check if secretary exists in seed data
        // Using a non-admin user to test forbidden
        using var client = await factory.CreateAuthenticatedClientAsync("henrique.lima@clinicaaurora.com", "ChangeMe123!");

        var response = await client.GetAsync("/tenant/integration");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed record TenantIntegrationResult(
        WhatsAppConfigResult? WhatsApp,
        PaymentGatewayConfigResult? PaymentGateway,
        NotificationConfigResult? Notification,
        BrandingResult? Branding,
        int DefaultAppointmentMinutes);
    private sealed record WhatsAppConfigResult(Guid Id, string PhoneNumberOfId, string? WABAId, bool IsEnabled);
    private sealed record PaymentGatewayConfigResult(Guid Id, string Provider, string Environment, bool IsEnabled);
    private sealed record NotificationConfigResult(Guid Id, int ReminderHoursBefore, string ChannelsJson, string? QuietHoursStart, string? QuietHoursEnd, bool EnableAutoReminders);
    private sealed record BrandingResult(Guid Id, string? LogoUrl, string? PrimaryColor, string? SecondaryColor, string? CustomClinicName);
}
