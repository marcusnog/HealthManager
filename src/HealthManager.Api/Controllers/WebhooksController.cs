using System.Text;
using HealthManager.Application;
using HealthManager.Api.Hubs;
using HealthManager.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("webhooks/payments")]
public sealed class WebhooksController(
    IApplicationDbContext dbContext,
    PaymentIntentService paymentIntentService,
    Dictionary<PaymentGatewayProvider, IPaymentGatewayHandler> handlers,
    IHubContext<PaymentHub> hubContext) : ControllerBase
{
    [HttpPost("{provider}")]
    public async Task<IActionResult> HandlePaymentWebhook(
        string provider,
        CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var payload = await reader.ReadToEndAsync(ct);

        if (!Enum.TryParse<PaymentGatewayProvider>(provider, ignoreCase: true, out var parsed)
            || !handlers.TryGetValue(parsed, out var handler))
            return BadRequest(new { error = $"Provider '{provider}' desconhecido." });

        var clinicIdHeader = Request.Headers["X-Clinic-Id"].FirstOrDefault();
        if (!Guid.TryParse(clinicIdHeader, out var clinicId))
            return BadRequest(new { error = "X-Clinic-Id header invalido." });

        var config = await dbContext.ClinicPaymentGatewayConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ClinicId == clinicId && x.IsEnabled, ct);
        if (config is null)
            return BadRequest(new { error = "Gateway nao configurado para esta clinica." });

        var signature = Request.Headers["X-Hub-Signature-256"].FirstOrDefault()
                     ?? Request.Headers["X-Signature"].FirstOrDefault();

        if (!handler.VerifySignature(payload, signature, config.WebhookSecret))
            return Unauthorized(new { error = "Assinatura invalida." });

        var result = await handler.ParseAsync(payload, ct);
        var intent = await paymentIntentService.ProcessWebhookAsync(result, clinicId, ct);

        if (intent is not null)
        {
            await hubContext.Clients.Group(clinicId.ToString())
                .SendAsync("PaymentStatusChanged", new
                {
                    intent.Id,
                    intent.Status,
                    intent.GatewayReference,
                    intent.Amount
                }, ct);
        }

        return Ok();
    }
}
