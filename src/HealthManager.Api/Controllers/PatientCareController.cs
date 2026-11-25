using HealthManager.Application;
using HealthManager.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Authorize(Policy = "ClinicAdminOrSecretary")]
[Route("patients/{patientId:guid}/care/{source}")]
public sealed class PatientCareController(PatientCareService service, ITenantProvider tenant) : ControllerBase
{
    private bool Allowed(string source) => tenant.Role != UserRole.Secretary || source is "espera" or "horarios";

    [HttpGet]
    public async Task<ActionResult<CareWorkspace>> Read(Guid patientId, string source, CancellationToken ct) =>
        Allowed(source) ? Ok(await service.ReadAsync(patientId, source, ct)) : Forbid();

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CareItem>> Save(Guid patientId, string source, Guid id, CareInput body, CancellationToken ct) =>
        Allowed(source) ? Ok(await service.SaveAsync(patientId, source, id, body, ct)) : Forbid();
}
