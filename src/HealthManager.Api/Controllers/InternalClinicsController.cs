using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Authorize(Policy = "PlatformAdminOnly")]
[Route("internal/clinics")]
public sealed class InternalClinicsController(ClinicProvisioningService clinicProvisioningService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ClinicProvisioningResponse>> CreateClinic([FromBody] CreateClinicRequest request, CancellationToken cancellationToken)
    {
        var response = await clinicProvisioningService.CreateClinicAsync(request, cancellationToken);
        return CreatedAtAction(nameof(CreateClinic), new { id = response.ClinicId }, response);
    }

    [HttpPost("{clinicId:guid}/users")]
    public async Task<ActionResult<UserResponse>> CreateUser(Guid clinicId, [FromBody] CreateClinicUserRequest request, CancellationToken cancellationToken)
    {
        var response = await clinicProvisioningService.CreateClinicUserAsync(clinicId, request, cancellationToken);
        return CreatedAtAction(nameof(CreateUser), new { id = response.Id }, response);
    }
}

