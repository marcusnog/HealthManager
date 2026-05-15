using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Authorize(Policy = "ClinicStaff")]
[Route("doctors")]
public sealed class DoctorsController(IDoctorService doctorService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DoctorResponse>>> List(CancellationToken cancellationToken)
        => Ok(await doctorService.ListAsync(cancellationToken));

    [HttpPost]
    public async Task<ActionResult<DoctorResponse>> Create([FromBody] CreateDoctorRequest request, CancellationToken cancellationToken)
    {
        var response = await doctorService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Create), new { id = response.Id }, response);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<DoctorResponse>> Update(Guid id, [FromBody] UpdateDoctorRequest request, CancellationToken cancellationToken)
        => Ok(await doctorService.UpdateAsync(id, request, cancellationToken));
}

