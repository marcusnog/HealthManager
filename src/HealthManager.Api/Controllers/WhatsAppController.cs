using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HealthManager.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("whatsapp")]
public sealed class WhatsAppController(WhatsAppWebhookService whatsAppWebhookService, IConfiguration configuration) : ControllerBase
{
    [HttpGet("webhook")]
    public IActionResult Verify([FromQuery(Name = "hub.mode")] string? mode, [FromQuery(Name = "hub.verify_token")] string? verifyToken, [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var configuredToken = configuration["WHATSAPP_VERIFY_TOKEN"];
        return mode == "subscribe" && !string.IsNullOrWhiteSpace(configuredToken) && verifyToken == configuredToken && !string.IsNullOrEmpty(challenge)
            ? Content(challenge, "text/plain") : Unauthorized();
    }

    [HttpPost("webhook")]
    [RequestSizeLimit(1_048_576)]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
    {
        var secret = configuration["WHATSAPP_APP_SECRET"];
        if (string.IsNullOrWhiteSpace(secret)) return StatusCode(503);
        var signature = Request.Headers["X-Hub-Signature-256"].ToString();
        if (!signature.StartsWith("sha256=", StringComparison.Ordinal) || signature.Length != 71) return Unauthorized();
        byte[] supplied;
        try { supplied = Convert.FromHexString(signature[7..]); }
        catch (FormatException) { return Unauthorized(); }
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await Request.Body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (body.Length + count > 1_048_576) return StatusCode(413);
            body.Write(buffer, 0, count);
        }
        var bytes = body.ToArray();
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), bytes);
        if (!CryptographicOperations.FixedTimeEquals(expected, supplied)) return Unauthorized();
        try
        {
            using var payload = JsonDocument.Parse(bytes);
            await whatsAppWebhookService.ProcessAsync(payload.RootElement, cancellationToken);
        }
        catch (JsonException) { return BadRequest("JSON Meta invalido."); }
        return Ok();
    }
}

