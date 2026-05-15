using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("whatsapp")]
public sealed class WhatsAppController(IWhatsAppWebhookService whatsAppWebhookService, IConfiguration configuration) : ControllerBase
{
    [HttpGet("webhook")]
    public IActionResult Verify([FromQuery(Name = "hub.verify_token")] string verifyToken, [FromQuery(Name = "hub.challenge")] string challenge)
    {
        var configuredToken = configuration["WHATSAPP_VERIFY_TOKEN"];
        return verifyToken == configuredToken ? Ok(challenge) : Unauthorized();
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook([FromBody] WhatsAppWebhookRequest request, CancellationToken cancellationToken)
    {
        await whatsAppWebhookService.ProcessAsync(request, cancellationToken);
        return Ok();
    }
}

