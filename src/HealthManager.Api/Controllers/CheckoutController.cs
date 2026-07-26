using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
public sealed class CheckoutController(CheckoutService checkoutService) : ControllerBase
{
    [HttpPost("checkout")]
    [Authorize(Policy = "ClinicStaff")]
    public async Task<ActionResult<CheckoutResponse>> CreateStaffCheckout([FromBody] CheckoutRequest request, CancellationToken ct)
    {
        var response = await checkoutService.CreateAsync(request, patientName: null, patientCpf: null, ct);
        return CreatedAtAction(nameof(GetCheckout), new { id = response.PaymentIntentId }, response);
    }

    [HttpPost("portal/checkout")]
    [Authorize(Policy = "PatientPortal")]
    public async Task<ActionResult<CheckoutResponse>> CreatePatientCheckout([FromBody] CheckoutRequest request, CancellationToken ct)
    {
        var response = await checkoutService.CreateForPatientAsync(request, ct);
        return CreatedAtAction(nameof(GetCheckout), new { id = response.PaymentIntentId }, response);
    }

    [HttpGet("checkout/{id:guid}")]
    [Authorize(Policy = "ClinicStaff")]
    public async Task<ActionResult<CheckoutResponse>> GetCheckout(Guid id, CancellationToken ct)
        => Ok(await checkoutService.GetAsync(id, ct));
}
