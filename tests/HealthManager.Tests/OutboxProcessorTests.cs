using FluentAssertions;
using HealthManager.Domain;
using HealthManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests;

public sealed class OutboxProcessorTests
{
    [Fact]
    public async Task ProcessPendingAsync_ShouldMarkEventsAsProcessed()
    {
        await using var dbContext = CreateDbContext();
        dbContext.OutboxEvents.Add(new OutboxEvent
        {
            EventType = "test.event",
            PayloadJson = "{}",
            Status = OutboxStatus.Pending
        });
        await dbContext.SaveChangesAsync();

        var processor = new HealthManager.Infrastructure.OutboxProcessor(dbContext);
        var count = await processor.ProcessPendingAsync(CancellationToken.None);

        count.Should().Be(1);
        var ev = dbContext.OutboxEvents.Single();
        ev.Status.Should().Be(OutboxStatus.Processed);
        ev.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldReturnZero_WhenNoPendingEvents()
    {
        await using var dbContext = CreateDbContext();
        var processor = new HealthManager.Infrastructure.OutboxProcessor(dbContext);
        var count = await processor.ProcessPendingAsync(CancellationToken.None);

        count.Should().Be(0);
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldRespectBatchLimit()
    {
        await using var dbContext = CreateDbContext();
        for (var i = 0; i < 30; i++)
        {
            dbContext.OutboxEvents.Add(new OutboxEvent
            {
                EventType = "test.event",
                PayloadJson = "{}",
                Status = OutboxStatus.Pending
            });
        }
        await dbContext.SaveChangesAsync();

        var processor = new HealthManager.Infrastructure.OutboxProcessor(dbContext);
        var count = await processor.ProcessPendingAsync(CancellationToken.None);

        count.Should().Be(25);
        dbContext.OutboxEvents.Count(x => x.Status == OutboxStatus.Processed).Should().Be(25);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }
}
