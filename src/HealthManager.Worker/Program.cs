using HealthManager.Application;
using HealthManager.Infrastructure;
using HealthManager.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["WORKER_METRICS_URL"] ?? "http://+:9091");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddHostedService<OutboxWorker>();
builder.Services.AddHostedService<PaymentStatusWorker>();

builder.Logging.AddConsole();

var app = builder.Build();
var startedAt = Stopwatch.GetTimestamp();
app.MapGet("/metrics", () =>
{
    using var process = Process.GetCurrentProcess();
    return Results.Text(FormattableString.Invariant($"""
        # TYPE process_uptime_seconds gauge
        process_uptime_seconds {Stopwatch.GetElapsedTime(startedAt).TotalSeconds}
        # TYPE process_cpu_seconds_total counter
        process_cpu_seconds_total {process.TotalProcessorTime.TotalSeconds}
        # TYPE process_resident_memory_bytes gauge
        process_resident_memory_bytes {process.WorkingSet64}

        """), "text/plain; version=0.0.4");
});
await app.RunAsync();
