using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests.Integration;

public sealed class WhatsAppWebhookEndpointsTests
{
    [Fact]
    public async Task Webhook_ShouldConfirmAppointmentAndPersistInboundMessage()
    {
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/whatsapp/webhook", new
        {
            clinicId = "11111111-1111-1111-1111-111111111111",
            appointmentId = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
            phone = "11999998888",
            message = "CONFIRMAR CONSULTA",
            providerMessageId = "meta-test-001"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await factory.WithDbContextAsync(async dbContext =>
        {
            var appointment = await dbContext.Appointments.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"));
            var inboundMessage = await dbContext.WhatsAppMessages.IgnoreQueryFilters()
                .SingleAsync(x => x.ProviderMessageId == "meta-test-001");
            var webhookEvent = await dbContext.WebhookEvents.IgnoreQueryFilters()
                .OrderByDescending(x => x.CreatedAt)
                .FirstAsync();
            var outboxEvent = await dbContext.OutboxEvents.IgnoreQueryFilters()
                .OrderByDescending(x => x.CreatedAt)
                .FirstAsync(x => x.EventType == "whatsapp.webhook.processed");

            appointment.Status.Should().Be(AppointmentStatus.Confirmed);
            appointment.ConfirmationStatus.Should().Be(ConfirmationStatus.Confirmed);
            inboundMessage.Direction.Should().Be(MessageDirection.Inbound);
            inboundMessage.Status.Should().Be(WhatsAppMessageStatus.Delivered);
            webhookEvent.ClinicId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
            outboxEvent.ClinicId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        });
    }
}
