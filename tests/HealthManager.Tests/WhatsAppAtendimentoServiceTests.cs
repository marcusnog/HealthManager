using FluentAssertions;
using HealthManager.Application;
using HealthManager.Domain;
using HealthManager.Infrastructure;

namespace HealthManager.Tests;

public sealed class WhatsAppAtendimentoServiceTests
{
    [Fact]
    public async Task ListAsync_GroupsMessagesByPhoneAndUsesLatestMessage()
    {
        await using var db = TestHelpers.CreateDbContext();
        var clinicId = Guid.NewGuid();
        db.WhatsAppMessages.Add(new WhatsAppMessage { ClinicId = clinicId, Phone = "11999998888", Message = "primeira", Direction = MessageDirection.Inbound });
        await db.SaveChangesAsync();
        await Task.Delay(10);
        db.WhatsAppMessages.Add(new WhatsAppMessage { ClinicId = clinicId, Phone = "5511999998888", Message = "ultima", Direction = MessageDirection.Outbound });
        await db.SaveChangesAsync();

        var messaging = new WhatsAppMessagingService(db, new FakeMetaCloudApiClient(), new OutboxService(db));
        var service = new WhatsAppAtendimentoService(db, new FakeTenantProvider(clinicId), messaging);

        var result = await service.ListAsync(new WhatsAppConversationQuery(), default);

        result.Total.Should().Be(1);
        result.Items.Single().LastMessage.Should().Be("ultima");
    }
}
