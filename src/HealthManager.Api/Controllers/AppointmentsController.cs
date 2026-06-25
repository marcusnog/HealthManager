using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Authorize(Policy = "ClinicStaff")]
[Route("appointments")]
public sealed class AppointmentsController(IAppointmentService appointmentService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AppointmentResponse>>> List([FromQuery] AppointmentQuery query, CancellationToken cancellationToken)
        => Ok(await appointmentService.ListAsync(query, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<AppointmentResponse>> Create([FromBody] CreateAppointmentRequest request, CancellationToken cancellationToken)
    {
        var response = await appointmentService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Create), new { id = response.Id }, response);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<AppointmentResponse>> Update(Guid id, [FromBody] UpdateAppointmentRequest request, CancellationToken cancellationToken)
        => Ok(await appointmentService.UpdateAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/confirm")]
    public async Task<ActionResult<AppointmentResponse>> Confirm(Guid id, CancellationToken cancellationToken)
        => Ok(await appointmentService.ConfirmAsync(id, cancellationToken));

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<AppointmentResponse>> Cancel(Guid id, CancellationToken cancellationToken)
        => Ok(await appointmentService.CancelAsync(id, cancellationToken));
}

