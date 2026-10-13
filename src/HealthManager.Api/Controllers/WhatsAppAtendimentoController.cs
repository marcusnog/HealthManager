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
    public async Task<ActionResult<PagedResult<WhatsAppMessageResponse>>> Messages(string phone, CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var result = await service.MessagesAsync(phone, cancellationToken, page, pageSize);
        // The previous frontend expects an array until its independent deployment completes.
        if (!Request.Query.ContainsKey("page") && !Request.Query.ContainsKey("pageSize"))
            return Ok(result.Items.Reverse());
        return Ok(result);
    }

    [HttpPost("{phone}/messages")]
    public async Task<ActionResult<WhatsAppMessageResponse>> Send(string phone, [FromBody] SendWhatsAppMessageRequest request, CancellationToken cancellationToken)
        => Ok(await service.SendAsync(phone, request, cancellationToken));
}
