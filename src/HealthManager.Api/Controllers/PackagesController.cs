using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Authorize(Policy = "ClinicStaff")]
[Route("packages")]
public sealed class PackagesController(PackageService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<PackageResponse>>> List([FromQuery] PackageQuery query, CancellationToken ct) => Ok(await service.ListAsync(query, ct));

    [HttpPost, Authorize(Policy = "ClinicAdminOrSecretary")]
    public async Task<ActionResult<PackageResponse>> Create(PackageRequest request, CancellationToken ct)
    {
        var response = await service.CreateAsync(request, ct);
        return Created($"/packages/{response.Id}", response);
    }

    [HttpPut("{id:guid}"), Authorize(Policy = "ClinicAdminOrSecretary")]
    public async Task<ActionResult<PackageResponse>> Update(Guid id, PackageRequest request, CancellationToken ct) => Ok(await service.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}"), Authorize(Policy = "ClinicAdminOrSecretary")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await service.DeleteAsync(id, ct); return NoContent(); }
}
