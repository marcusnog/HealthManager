using FluentAssertions;
using HealthManager.Application;
using HealthManager.Domain;
using HealthManager.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests;

public sealed class WhatsAppAtendimentoServiceTests
{
    [Fact]
    public async Task History_IsPagedAndIsolated()
    {
        await using var db = TestHelpers.CreateDbContext();
        var clinic = Guid.NewGuid();
        for (var i = 0; i < 7; i++) db.WhatsAppMessages.Add(new WhatsAppMessage { ClinicId = clinic, Phone = "5511999998888", Message = i.ToString() });
        db.WhatsAppMessages.Add(new WhatsAppMessage { ClinicId = Guid.NewGuid(), Phone = "5511999998888", Message = "foreign" });
        await db.SaveChangesAsync();
        var service = new WhatsAppAtendimentoService(db, new FakeTenantProvider(clinic), new WhatsAppMessagingService(db, new FakeMetaCloudApiClient(), new OutboxService(db)));
        var first = await service.MessagesAsync("11999998888", default, 1, 3);
        var second = await service.MessagesAsync("11999998888", default, 2, 3);
        first.Total.Should().Be(7);
        first.Items.Should().HaveCount(3).And.NotContain(x => x.Message == "foreign");
        first.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Send_PersistsBeforeProvider_AndRetryReturnsSameAttempt(bool uncertain)
    {
        await using var db = TestHelpers.CreateDbContext();
        var clinic = Guid.NewGuid();
        db.ClinicWhatsAppConfigs.Add(new ClinicWhatsAppConfig { ClinicId = clinic, PhoneNumberOfId = "meta", IsEnabled = true });
        db.WhatsAppMessages.Add(new WhatsAppMessage { ClinicId = clinic, Phone = "5555999998888", Direction = MessageDirection.Inbound, ProviderTimestamp = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var provider = new InspectingClient(async () => {
            (await db.WhatsAppMessages.CountAsync(x => x.Status == WhatsAppMessageStatus.Pending && x.Direction == MessageDirection.Outbound)).Should().Be(1);
        }, uncertain);
        var service = new WhatsAppAtendimentoService(db, new FakeTenantProvider(clinic), new WhatsAppMessagingService(db, provider, new OutboxService(db)));
        var request = new SendWhatsAppMessageRequest(" Olá ", Guid.NewGuid());
        var sent = await service.SendAsync("(55) 99999-8888", request, default);
        var replay = await service.SendAsync("(55) 99999-8888", request, default);
        replay.Id.Should().Be(sent.Id);
        sent.Status.Should().Be(uncertain ? WhatsAppMessageStatus.Pending : WhatsAppMessageStatus.Sent);
        sent.Phone.Should().Be("5555999998888");
        provider.Calls.Should().Be(1);
        var conflict = () => service.SendAsync(sent.Phone, request with { Message = "Outra" }, default);
        await conflict.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Send_RejectsExpiredWindowAndMissingTenant()
    {
        await using var db = TestHelpers.CreateDbContext();
        var clinic = Guid.NewGuid();
        db.WhatsAppMessages.Add(new WhatsAppMessage { ClinicId = clinic, Phone = "5511999998888", Direction = MessageDirection.Inbound, ProviderTimestamp = DateTimeOffset.UtcNow.AddHours(-25) });
        await db.SaveChangesAsync();
        var messaging = new WhatsAppMessagingService(db, new FakeMetaCloudApiClient(), new OutboxService(db));
        var service = new WhatsAppAtendimentoService(db, new FakeTenantProvider(clinic), messaging);
        await FluentActions.Invoking(() => service.SendAsync("11999998888", new("Olá"), default)).Should().ThrowAsync<InvalidOperationException>().WithMessage("*Janela*");
        var anonymous = new WhatsAppAtendimentoService(db, new FakeTenantProvider(null), messaging);
        await FluentActions.Invoking(() => anonymous.ListAsync(new(), default)).Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class InspectingClient(Func<Task> inspect, bool uncertain) : IMetaCloudApiClient
    {
        public int Calls { get; private set; }
        public async Task<MetaMessageResponse> SendTextAsync(string phoneNumberId, string accessToken, string to, string text, CancellationToken cancellationToken)
        {
            Calls++;
            await inspect();
            if (uncertain) throw new HttpRequestException("timeout");
            return new() { Success = true, MessageId = Guid.NewGuid().ToString() };
        }
        public Task<MetaMessageResponse> SendTemplateAsync(string phoneNumberId, string accessToken, string to, string templateName, string languageCode, Dictionary<string, string>? parameters, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    [Fact]
    public async Task ListAsync_GroupsMessagesByPhoneAndUsesLatestMessage()
    {
        await using var db = TestHelpers.CreateDbContext();
        var clinicId = Guid.NewGuid();
        db.WhatsAppMessages.Add(new WhatsAppMessage { ClinicId = clinicId, Phone = "5511999998888", Message = "primeira", Direction = MessageDirection.Inbound });
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
