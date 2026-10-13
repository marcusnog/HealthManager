using System.Text.Json;
using FluentAssertions;
using HealthManager.Application;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests;

public sealed class WhatsAppWebhookServiceTests
{
    internal static JsonElement Payload(string number, string messageId = "wamid.1", string status = "", long? timestamp = null) => JsonSerializer.SerializeToElement(new
    {
        @object = "whatsapp_business_account",
        entry = new[] { new { changes = new[] { new { field = "messages", value = new {
            metadata = new { phone_number_id = number },
            messages = status == "" ? new[] { new { id = messageId, from = "5511999998888", type = "text", text = new { body = "CONFIRMAR CONSULTA" }, timestamp = (timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString() } } : [],
            statuses = status != "" ? new[] { new { id = messageId, status } } : []
        } } } } }
    });

    [Fact]
    public async Task VerifiedNumber_OverridesTenantContext_AndReplayDoesNotDuplicate()
    {
        var clinicA = Guid.NewGuid(); var clinicB = Guid.NewGuid();
        await using var db = TestHelpers.CreateDbContext(new FakeTenantProvider(clinicA));
        db.Clinics.AddRange(new Clinic { Id = clinicA, Name = "A", Slug = "a" }, new Clinic { Id = clinicB, Name = "B", Slug = "b" });
        db.ClinicWhatsAppConfigs.Add(new ClinicWhatsAppConfig { ClinicId = clinicB, PhoneNumberOfId = "number-b", IsEnabled = true });
        await db.SaveChangesAsync();
        var service = new WhatsAppWebhookService(db);
        await service.ProcessAsync(Payload("number-b"), default);
        await service.ProcessAsync(Payload("number-b"), default);
        var messages = await db.WhatsAppMessages.IgnoreQueryFilters().ToListAsync();
        messages.Should().ContainSingle().Which.ClinicId.Should().Be(clinicB);
        messages[0].ProviderTimestamp.Should().NotBeNull();
        db.OutboxEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownOrDisabledNumber_DoesNotPersist(bool disabled)
    {
        await using var db = TestHelpers.CreateDbContext();
        if (disabled) db.ClinicWhatsAppConfigs.Add(new ClinicWhatsAppConfig { ClinicId = Guid.NewGuid(), PhoneNumberOfId = "unknown", IsEnabled = false });
        await db.SaveChangesAsync();
        await new WhatsAppWebhookService(db).ProcessAsync(Payload("unknown"), default);
        db.WhatsAppMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task Status_IsMonotonic_AndCannotUpdateOtherClinic()
    {
        await using var db = TestHelpers.CreateDbContext();
        var clinic = Guid.NewGuid(); var other = Guid.NewGuid();
        db.Clinics.Add(new Clinic { Id = clinic, Name = "A", Slug = "a" });
        db.ClinicWhatsAppConfigs.Add(new ClinicWhatsAppConfig { ClinicId = clinic, PhoneNumberOfId = "number", IsEnabled = true });
        var message = new WhatsAppMessage { ClinicId = clinic, Phone = "5511999998888", ProviderMessageId = "id", Status = WhatsAppMessageStatus.Sent };
        var foreign = new WhatsAppMessage { ClinicId = other, Phone = message.Phone, ProviderMessageId = "id", Status = WhatsAppMessageStatus.Sent };
        db.WhatsAppMessages.AddRange(message, foreign);
        await db.SaveChangesAsync();
        var service = new WhatsAppWebhookService(db);
        foreach (var status in new[] { "read", "delivered", "failed", "sent", "read" })
            await service.ProcessAsync(Payload("number", "id", status), default);
        message.Status.Should().Be(WhatsAppMessageStatus.Read);
        foreign.Status.Should().Be(WhatsAppMessageStatus.Sent);
        db.WhatsAppMessages.Should().HaveCount(2);
    }

    [Fact]
    public async Task LegacyPayload_IsRejected()
    {
        await using var db = TestHelpers.CreateDbContext();
        var service = new WhatsAppWebhookService(db);
        var act = () => service.ProcessAsync(JsonSerializer.SerializeToElement(new { clinicId = Guid.NewGuid(), message = "CONFIRM" }), default);
        await act.Should().ThrowAsync<InvalidOperationException>();
        db.WhatsAppMessages.Should().BeEmpty();
    }
}
