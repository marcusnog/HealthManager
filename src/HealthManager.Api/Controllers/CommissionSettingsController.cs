using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Authorize(Policy = "ClinicAdmin")]
[Route("financial/commission-settings")]
public sealed class CommissionSettingsController(CommissionSettingsService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CommissionSettings>> Get(CancellationToken ct) => Ok(await service.GetAsync(ct));

    [HttpPut]
    public async Task<ActionResult<CommissionSettings>> Update([FromBody] CommissionSettings request, CancellationToken ct) =>
        Ok(await service.UpdateAsync(request, ct));
}
