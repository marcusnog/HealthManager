using HealthManager.Application;
using HealthManager.Infrastructure;
using HealthManager.Worker;
using Microsoft.Extensions.Hosting;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<OutboxWorker>();

builder.Services.AddSerilog(configuration => configuration.WriteTo.Console());

var host = builder.Build();
await host.RunAsync();
