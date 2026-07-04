using HealthManager.Application;
using HealthManager.Infrastructure;
using HealthManager.Worker;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddHostedService<OutboxWorker>();

builder.Logging.AddConsole();

var host = builder.Build();
await host.RunAsync();
