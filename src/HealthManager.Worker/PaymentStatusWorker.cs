using HealthManager.Application;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Worker;

public sealed class PaymentStatusWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<PaymentStatusWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                var gatewayClient = scope.ServiceProvider.GetRequiredService<IPaymentGatewayClient>();
                var paymentIntentService = scope.ServiceProvider.GetRequiredService<PaymentIntentService>();

                var pendingIntents = await dbContext.PaymentIntents
                    .Where(x => x.Status == PaymentIntentStatus.Created || x.Status == PaymentIntentStatus.Processing)
                    .Take(25)
                    .ToListAsync(stoppingToken);

                foreach (var intent in pendingIntents)
                {
                    var config = await dbContext.ClinicPaymentGatewayConfigs
                        .FirstOrDefaultAsync(x => x.ClinicId == intent.ClinicId && x.IsEnabled, stoppingToken);

                    if (config is null || intent.ClinicId is null) continue;

                    var status = await gatewayClient.GetPaymentStatusAsync(
                        intent.GatewayReference ?? intent.Id.ToString(),
                        config.Provider,
                        intent.ClinicId.Value,
                        stoppingToken);

                    var result = new WebhookPaymentResult
                    {
                        Status = status.Status,
                        GatewayReference = intent.GatewayReference,
                        IdempotencyKey = intent.IdempotencyKey,
                        FailureReason = status.FailureReason
                    };

                    await paymentIntentService.ProcessWebhookAsync(result, intent.ClinicId.Value, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Payment status worker iteration failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
