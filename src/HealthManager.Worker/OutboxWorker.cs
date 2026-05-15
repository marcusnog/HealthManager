using HealthManager.Infrastructure;

namespace HealthManager.Worker;

public sealed class OutboxWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
                var processedCount = await processor.ProcessPendingAsync(stoppingToken);

                if (processedCount > 0)
                {
                    logger.LogInformation("Processed {ProcessedCount} outbox events.", processedCount);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox worker iteration failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}

