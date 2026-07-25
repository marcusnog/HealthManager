using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HealthManager.Api.Hubs;

[Authorize(Policy = "ClinicStaff")]
public sealed class PaymentHub : Hub
{
    public async Task JoinClinicGroup(string clinicId)
    {
        var userClinicId = Context.User?.FindFirst("clinic_id")?.Value;
        if (Context.User?.IsInRole("PlatformAdmin") == true || userClinicId == clinicId)
            await Groups.AddToGroupAsync(Context.ConnectionId, clinicId);
    }

    public async Task LeaveClinicGroup(string clinicId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, clinicId);
}
