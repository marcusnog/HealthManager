using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Authorize(Policy = "ClinicStaff")]
[Route("tenant/settings")]
public sealed class TenantSettingsController(TenantSettingsService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TenantSettingsResponse>> Get(CancellationToken ct) =>
        Ok(await service.GetAsync(ct));

    [HttpPut]
    [Authorize(Policy = "ClinicAdmin")]
    public async Task<ActionResult<TenantSettingsResponse>> Update(
        [FromBody] UpdateTenantSettingsRequest request,
        CancellationToken ct) =>
        Ok(await service.UpdateAsync(request, ct));
}
