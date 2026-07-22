using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Authorize(Policy = "ClinicStaff")]
[Route("appointment-types")]
public sealed class AppointmentTypesController(AppointmentTypeService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AppointmentTypeResponse>>> List([FromQuery] AppointmentTypeQuery query, CancellationToken ct)
        => Ok(await service.ListAsync(query, ct));

    [HttpPost]
    [Authorize(Policy = "ClinicAdminOrSecretary")]
    public async Task<ActionResult<AppointmentTypeResponse>> Create([FromBody] AppointmentTypeRequest request, CancellationToken ct)
    {
        var response = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Create), new { id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "ClinicAdminOrSecretary")]
    public async Task<ActionResult<AppointmentTypeResponse>> Update(Guid id, [FromBody] AppointmentTypeRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "ClinicAdminOrSecretary")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }
}
