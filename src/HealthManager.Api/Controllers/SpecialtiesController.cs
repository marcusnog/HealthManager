using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Authorize(Policy = "ClinicAdminOrSecretary")]
[Route("specialties")]
public sealed class SpecialtiesController(SpecialtyService specialtyService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<SpecialtyResponse>>> List([FromQuery] SpecialtyQuery query, CancellationToken cancellationToken)
        => Ok(await specialtyService.ListAsync(query, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<SpecialtyResponse>> Create([FromBody] CreateSpecialtyRequest request, CancellationToken cancellationToken)
    {
        var response = await specialtyService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Create), new { id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SpecialtyResponse>> Update(Guid id, [FromBody] UpdateSpecialtyRequest request, CancellationToken cancellationToken)
        => Ok(await specialtyService.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await specialtyService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
