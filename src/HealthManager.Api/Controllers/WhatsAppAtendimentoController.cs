using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Authorize(Policy = "ClinicAdminOrSecretary")]
[Route("whatsapp/conversations")]
public sealed class WhatsAppAtendimentoController(WhatsAppAtendimentoService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<WhatsAppConversationResponse>>> List([FromQuery] WhatsAppConversationQuery query, CancellationToken cancellationToken)
        => Ok(await service.ListAsync(query, cancellationToken));

    [HttpGet("{phone}/messages")]
    public async Task<ActionResult<IReadOnlyList<WhatsAppMessageResponse>>> Messages(string phone, CancellationToken cancellationToken)
        => Ok(await service.MessagesAsync(phone, cancellationToken));

    [HttpPost("{phone}/messages")]
    public async Task<ActionResult<WhatsAppMessageResponse>> Send(string phone, [FromBody] SendWhatsAppMessageRequest request, CancellationToken cancellationToken)
        => Ok(await service.SendAsync(phone, request, cancellationToken));
}
