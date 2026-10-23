using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin,Doctor")]
[Route("clinical-catalog")]
public sealed class ClinicalCatalogController(ClinicalWorkspaceService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ClinicalCatalogResponse[]>> List(string? kind, string? search, CancellationToken ct) => Ok(await service.ListCatalogAsync(kind, search, ct));
    [HttpPost]
    public async Task<ActionResult<ClinicalCatalogResponse>> Create(SaveClinicalCatalogRequest request, CancellationToken ct)
    {
        var result = await service.SaveCatalogAsync(null, request, ct);
        return Created($"/clinical-catalog/{result.Id}", result);
    }
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ClinicalCatalogResponse>> Update(Guid id, SaveClinicalCatalogRequest request, CancellationToken ct) => Ok(await service.SaveCatalogAsync(id, request, ct));
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid revision, CancellationToken ct) { await service.DeleteCatalogAsync(id, revision, ct); return NoContent(); }
}

[ApiController]
[Authorize(Policy = "ClinicStaff")]
public sealed class ClinicalEntriesController(ClinicalWorkspaceService service) : ControllerBase
{
    [HttpGet("appointments/{appointmentId:guid}/clinical-entries")]
    public async Task<ActionResult<ClinicalEntryResponse[]>> List(Guid appointmentId, CancellationToken ct) => Ok(await service.ListAppointmentAsync(appointmentId, ct));
    [HttpGet("patients/{patientId:guid}/clinical-entries")]
    public async Task<ActionResult<PagedResult<ClinicalEntryResponse>>> History(Guid patientId, CancellationToken ct, int page = 1, int pageSize = 50, string? kind = null) => Ok(await service.ListPatientAsync(patientId, page, pageSize, kind, ct));
    [HttpPost("appointments/{appointmentId:guid}/clinical-entries")]
    [Authorize(Policy = "DoctorOnly")]
    public async Task<ActionResult<ClinicalEntryResponse>> Create(Guid appointmentId, CreateClinicalEntryRequest request, CancellationToken ct)
    {
        var result = await service.CreateEntryAsync(appointmentId, request, ct);
        return Created($"/appointments/{appointmentId}/clinical-entries", result);
    }
}
