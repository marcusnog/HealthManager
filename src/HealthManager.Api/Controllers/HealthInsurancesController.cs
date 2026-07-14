using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Authorize(Policy = "ClinicAdminOrSecretary")]
[Route("health-insurances")]
public sealed class HealthInsurancesController(HealthInsuranceService healthInsuranceService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<HealthInsuranceResponse>>> List([FromQuery] HealthInsuranceQuery query, CancellationToken cancellationToken)
        => Ok(await healthInsuranceService.ListAsync(query, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<HealthInsuranceResponse>> Create([FromBody] CreateHealthInsuranceRequest request, CancellationToken cancellationToken)
    {
        var response = await healthInsuranceService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Create), new { id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<HealthInsuranceResponse>> Update(Guid id, [FromBody] UpdateHealthInsuranceRequest request, CancellationToken cancellationToken)
        => Ok(await healthInsuranceService.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await healthInsuranceService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
