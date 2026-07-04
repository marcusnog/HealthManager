using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Route("portal")]
public sealed class PatientPortalController(PatientPortalService portalService) : ControllerBase
{
    [HttpPost("auth")]
    [AllowAnonymous]
    public async Task<ActionResult<PatientPortalAuthResponse>> Login([FromBody] PatientPortalLoginRequest request, CancellationToken cancellationToken)
        => Ok(await portalService.LoginAsync(request, cancellationToken));

    [HttpGet("me")]
    [Authorize(Policy = "PatientPortal")]
    public async Task<ActionResult<PatientPortalProfileResponse>> GetProfile(CancellationToken cancellationToken)
        => Ok(await portalService.GetProfileAsync(cancellationToken));

    [HttpGet("appointments")]
    [Authorize(Policy = "PatientPortal")]
    public async Task<ActionResult<IReadOnlyList<PatientPortalAppointmentResponse>>> GetAppointments(CancellationToken cancellationToken)
        => Ok(await portalService.GetAppointmentsAsync(cancellationToken));

    [HttpGet("receivables")]
    [Authorize(Policy = "PatientPortal")]
    public async Task<ActionResult<IReadOnlyList<PatientPortalReceivableResponse>>> GetReceivables(CancellationToken cancellationToken)
        => Ok(await portalService.GetReceivablesAsync(cancellationToken));

    [HttpGet("documents")]
    [Authorize(Policy = "PatientPortal")]
    public async Task<ActionResult<IReadOnlyList<PatientDocumentResponse>>> GetDocuments(CancellationToken cancellationToken)
        => Ok(await portalService.GetDocumentsAsync(cancellationToken));
}
