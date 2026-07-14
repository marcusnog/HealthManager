using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Authorize(Policy = "ClinicStaff")]
[Route("doctors")]
public sealed class DoctorsController(DoctorService doctorService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<DoctorResponse>>> List([FromQuery] DoctorQuery query, CancellationToken cancellationToken)
        => Ok(await doctorService.ListAsync(query, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<DoctorResponse>> Create([FromBody] CreateDoctorRequest request, CancellationToken cancellationToken)
    {
        var response = await doctorService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Create), new { id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DoctorResponse>> Update(Guid id, [FromBody] UpdateDoctorRequest request, CancellationToken cancellationToken)
        => Ok(await doctorService.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await doctorService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
