using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Authorize(Policy = "ClinicStaff")]
[Route("patients")]
public sealed class PatientsController(IPatientService patientService, IPatientPortalService portalService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<PatientResponse>>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null, CancellationToken cancellationToken = default)
        => Ok(await patientService.ListAsync(new PatientQuery(page, pageSize, search), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<PatientResponse>> Create([FromBody] CreatePatientRequest request, CancellationToken cancellationToken)
    {
        var response = await patientService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Create), new { id = response.Id }, response);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<PatientResponse>> Update(Guid id, [FromBody] UpdatePatientRequest request, CancellationToken cancellationToken)
        => Ok(await patientService.UpdateAsync(id, request, cancellationToken));

    [HttpGet("{id:guid}/documents")]
    public async Task<ActionResult<IReadOnlyList<PatientDocumentResponse>>> ListDocuments(Guid id, CancellationToken cancellationToken)
        => Ok(await patientService.ListDocumentsAsync(id, cancellationToken));

    [HttpPost("{id:guid}/documents/upload")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<PatientDocumentResponse>> UploadDocument(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length <= 0)
        {
            return BadRequest("Arquivo obrigatorio.");
        }

        await using var content = file.OpenReadStream();
        var response = await patientService.UploadDocumentAsync(
            id,
            new UploadPatientDocumentRequest(file.FileName, file.ContentType, file.Length, content),
            cancellationToken);

        return Ok(response);
    }

    [HttpGet("{id:guid}/documents/{documentId:guid}/download")]
    public async Task<IActionResult> DownloadDocument(Guid id, Guid documentId, CancellationToken cancellationToken)
    {
        var response = await patientService.DownloadDocumentAsync(id, documentId, cancellationToken);
        return File(response.Content, response.ContentType, response.FileName);
    }

    [HttpDelete("{id:guid}/documents/{documentId:guid}")]
    public async Task<IActionResult> DeleteDocument(Guid id, Guid documentId, CancellationToken cancellationToken)
    {
        await patientService.DeleteDocumentAsync(id, documentId, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/documents")]
    public async Task<ActionResult<PatientDocumentResponse>> AddDocument(Guid id, [FromBody] CreatePatientDocumentRequest request, CancellationToken cancellationToken)
        => Ok(await patientService.AddDocumentAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/access-token/regenerate")]
    [Authorize(Policy = "ClinicAdminOrSecretary")]
    public async Task<ActionResult<Guid>> RegenerateAccessToken(Guid id, CancellationToken cancellationToken)
        => Ok(await portalService.RegenerateAccessTokenAsync(id, cancellationToken));
}
