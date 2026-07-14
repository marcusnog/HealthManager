using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Authorize(Policy = "ClinicStaff")]
[Route("doctor-availabilities")]
public sealed class DoctorAvailabilitiesController(DoctorAvailabilityService availabilityService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<DoctorAvailabilityResponse>>> List([FromQuery] AvailabilityQuery query, CancellationToken cancellationToken)
        => Ok(await availabilityService.ListAsync(query, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<DoctorAvailabilityResponse>> Create([FromBody] CreateAvailabilityRequest request, CancellationToken cancellationToken)
    {
        var response = await availabilityService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Create), new { id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DoctorAvailabilityResponse>> Update(Guid id, [FromBody] UpdateAvailabilityRequest request, CancellationToken cancellationToken)
        => Ok(await availabilityService.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await availabilityService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
