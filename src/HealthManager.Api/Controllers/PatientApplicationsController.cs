using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Authorize(Policy = "ClinicStaff")]
[Route("patients/{patientId:guid}/applications")]
public sealed class PatientApplicationsController(PatientApplicationService service) : ControllerBase
{
    [HttpGet("balance")]
    public async Task<ActionResult<IReadOnlyList<PatientApplicationBalanceResponse>>> ListBalance(Guid patientId, CancellationToken ct)
        => Ok(await service.ListBalancesAsync(patientId, ct));

    [HttpPost("balance"), Authorize(Policy = "ClinicAdminOrSecretary")]
    public async Task<ActionResult<IReadOnlyList<PatientApplicationBalanceResponse>>> GrantBalance(Guid patientId, GrantApplicationBalanceRequest request, CancellationToken ct)
    {
        var response = await service.GrantBalanceAsync(patientId, request, ct);
        return Created($"/patients/{patientId}/applications/balance", response);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PatientApplicationResponse>>> ListApplications(Guid patientId, CancellationToken ct)
        => Ok(await service.ListApplicationsAsync(patientId, ct));

    [HttpPost, Authorize(Policy = "DoctorOnly")]
    public async Task<ActionResult<PatientApplicationResponse>> RecordApplication(Guid patientId, PatientApplicationRequest request, CancellationToken ct)
    {
        var response = await service.RecordApplicationAsync(patientId, request, ct);
        return Created($"/patients/{patientId}/applications/{response.Id}", response);
    }
}