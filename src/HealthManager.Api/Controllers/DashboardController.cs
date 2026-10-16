using HealthManager.Application;
using HealthManager.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Authorize(Policy = "ClinicStaff")]
[Route("dashboard")]
public sealed class DashboardController(DashboardService dashboardService) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryResponse>> GetSummary([FromQuery] Guid? doctorId = null, CancellationToken cancellationToken = default)
    {
        var summary = await dashboardService.GetSummaryAsync(doctorId, cancellationToken);
        if (User.HasClaim(Permissions.ClaimType, "screen.restricted") && !User.HasClaim(Permissions.ClaimType, Permissions.FinanceSummaryView))
            summary = summary with { MonthlyRevenue = 0, MonthlyExpenses = 0, MonthlyBalance = 0 };
        return Ok(summary);
    }
}

