using HealthManager.Application;
using HealthManager.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Authorize(Policy = "ClinicStaff")]
[Route("appointments/{appointmentId:guid}/clinical-record")]
public sealed class ClinicalRecordsController(ClinicalRecordService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ClinicalRecordResponse>> Get(Guid appointmentId, CancellationToken ct)
    {
        var result = await service.GetByAppointmentAsync(appointmentId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = "ClinicStaff")]
    public async Task<ActionResult<ClinicalRecordResponse>> Create(Guid appointmentId, [FromBody] CreateClinicalRecordRequest request, CancellationToken ct)
    {
        var response = await service.CreateAsync(appointmentId, request, ct);
        return CreatedAtAction(nameof(Get), new { appointmentId }, response);
    }

    [HttpPatch]
    [Authorize(Policy = "ClinicStaff")]
    public async Task<ActionResult<ClinicalRecordResponse>> Update(Guid appointmentId, [FromBody] UpdateClinicalRecordRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(appointmentId, request, ct));

    [HttpPost("finalize")]
    [Authorize(Policy = "ClinicStaff")]
    public async Task<ActionResult<ClinicalRecordResponse>> Finalize(Guid appointmentId, CancellationToken ct)
        => Ok(await service.FinalizeAsync(appointmentId, ct));

    [HttpPost("addendum")]
    public async Task<ActionResult<ClinicalRecordAddendumResponse>> AddAddendum(Guid appointmentId, [FromBody] CreateAddendumRequest request, CancellationToken ct)
    {
        var response = await service.AddAddendumAsync(appointmentId, request, ct);
        return CreatedAtAction(nameof(ListAddendums), new { appointmentId }, response);
    }

    [HttpGet("addendums")]
    public async Task<ActionResult<IReadOnlyList<ClinicalRecordAddendumResponse>>> ListAddendums(Guid appointmentId, CancellationToken ct)
        => Ok(await service.ListAddendumsAsync(appointmentId, ct));
}
