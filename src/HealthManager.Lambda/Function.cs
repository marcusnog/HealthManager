using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using HealthManager.Application;
using HealthManager.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.Json;

// DI container built once per cold start, reused across warm invocations.
var services = Host.CreateDefaultBuilder()
    .ConfigureServices((ctx, svc) =>
    {
        svc.AddApplication();
        svc.AddInfrastructure(ctx.Configuration, ctx.HostingEnvironment);
    })
    .Build()
    .Services;

var handler = async (JsonElement _, ILambdaContext context) =>
{
    using var scope = services.CreateScope();
    var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
    var count = await processor.ProcessPendingAsync(CancellationToken.None);
    context.Logger.LogInformation($"Processed {count} outbox events.");
};

await LambdaBootstrapBuilder
    .Create(handler, new DefaultLambdaJsonSerializer())
    .Build()
    .RunAsync();
