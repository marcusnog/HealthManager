using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Authorize(Policy = "ClinicStaff")]
[Route("payment-intents")]
public sealed class PaymentIntentsController(PaymentIntentService service) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<PaymentIntentResponse>> Create([FromBody] CreatePaymentIntentRequest request, CancellationToken ct)
    {
        var response = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(List), new { }, response);
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<PaymentIntentResponse>>> List([FromQuery] PaymentIntentQuery query, CancellationToken ct)
        => Ok(await service.ListAsync(query, ct));

    [HttpPost("{id:guid}/confirm")]
    [Authorize(Policy = "ClinicAdmin")]
    public async Task<ActionResult<PaymentIntentResponse>> Confirm(Guid id, CancellationToken ct)
        => Ok(await service.ConfirmAsync(id, ct));

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = "ClinicAdmin")]
    public async Task<ActionResult<PaymentIntentResponse>> Cancel(Guid id, CancellationToken ct)
        => Ok(await service.CancelAsync(id, ct));
}
