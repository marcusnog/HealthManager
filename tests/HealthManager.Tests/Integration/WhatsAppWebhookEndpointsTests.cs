using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests.Integration;

public sealed class WhatsAppWebhookEndpointsTests
{
    [Fact]
    public async Task History_PreservesLegacyArray_AndReturnsPagesForNewClient()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        await factory.WithDbContextAsync(async db => {
            for (var i = 0; i < 55; i++) db.WhatsAppMessages.Add(new WhatsAppMessage {
                ClinicId = Guid.Parse("11111111-1111-1111-1111-111111111111"), Phone = "5511999998888", Message = i.ToString()
            });
            db.WhatsAppMessages.Add(new WhatsAppMessage { ClinicId = Guid.NewGuid(), Phone = "5511999998888", Message = "foreign" });
            await db.SaveChangesAsync();
        });
        var legacy = await client.GetFromJsonAsync<JsonElement>("/whatsapp/conversations/5511999998888/messages");
        legacy.ValueKind.Should().Be(JsonValueKind.Array);
        legacy.GetArrayLength().Should().Be(50);
        var paged = await client.GetFromJsonAsync<JsonElement>("/whatsapp/conversations/5511999998888/messages?page=1&pageSize=50");
        paged.GetProperty("total").GetInt32().Should().Be(55);
        legacy.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).Should()
            .Equal(paged.GetProperty("items").EnumerateArray().Reverse().Select(x => x.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task MissingAppSecret_FailsClosed()
    {
        await using var factory = new ApiTestFactory(whatsAppSecret: "");
        using var client = factory.CreateClient();
        using var request = Signed("{}");
        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sha256=invalid")]
    [InlineData("sha256=0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task UnsignedOrTamperedWebhook_DoesNotPersist(string? signature)
    {
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/whatsapp/webhook") { Content = JsonContent.Create(new { clinicId = Guid.NewGuid(), message = "CONFIRM" }) };
        if (signature != null) request.Headers.Add("X-Hub-Signature-256", signature);
        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await factory.WithDbContextAsync(async db => (await db.WhatsAppMessages.IgnoreQueryFilters().CountAsync()).Should().Be(0));
    }

    [Fact]
    public async Task SignedWebhook_PersistsOnce_WithoutChangingAppointment()
    {
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();
        var clinic = Guid.Parse("11111111-1111-1111-1111-111111111111");
        await factory.WithDbContextAsync(async db => {
            db.ClinicWhatsAppConfigs.Add(new ClinicWhatsAppConfig { ClinicId = clinic, PhoneNumberOfId = "number", IsEnabled = true });
            await db.SaveChangesAsync();
        });
        var body = JsonSerializer.Serialize(WhatsAppWebhookServiceTests.Payload("number"));
        client.DefaultRequestHeaders.Add("X-Clinic-Id", Guid.NewGuid().ToString());
        for (var i = 0; i < 2; i++)
        {
            using var request = Signed(body);
            (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        await factory.WithDbContextAsync(async db => {
            var message = await db.WhatsAppMessages.IgnoreQueryFilters().SingleAsync();
            message.ClinicId.Should().Be(clinic);
            var appointment = await db.Appointments.IgnoreQueryFilters().SingleAsync(x => x.Id == Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"));
            appointment.ConfirmationStatus.Should().Be(ConfirmationStatus.Pending);
        });
    }

    [Fact]
    public async Task SignedLegacyBody_IsRejected()
    {
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();
        using var request = Signed("{\"clinicId\":\"11111111-1111-1111-1111-111111111111\",\"message\":\"CONFIRM\"}");
        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Challenge_RequiresModeAndReturnsPlainText()
    {
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/whatsapp/webhook?hub.mode=subscribe&hub.verify_token=test-meta-verify-token&hub.challenge=12345");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
        (await response.Content.ReadAsStringAsync()).Should().Be("12345");
        (await client.GetAsync("/whatsapp/webhook?hub.verify_token=test-meta-verify-token&hub.challenge=12345")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static HttpRequestMessage Signed(string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/whatsapp/webhook") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes("test-meta-app-secret"), Encoding.UTF8.GetBytes(body));
        request.Headers.Add("X-Hub-Signature-256", "sha256=" + Convert.ToHexString(digest).ToLowerInvariant());
        return request;
    }
}
