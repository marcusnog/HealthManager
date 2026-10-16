using HealthManager.Application;
using HealthManager.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Authorize(Policy = "ClinicAdmin")]
[Route("users")]
public sealed class UsersController(UserManagementService service) : ControllerBase
{
    [HttpGet]
    public Task<ManagedUserResponse[]> List(CancellationToken ct) => service.ListAsync(ct);

    [HttpGet("access-profiles")]
    public AccessProfileResponse[] Profiles() => new[] { UserRole.Admin, UserRole.Secretary, UserRole.Doctor }
        .Select(role => new AccessProfileResponse(role, ScreenAccess.Available(role), ScreenAccess.Defaults(role))).ToArray();

    [HttpPost]
    public async Task<ActionResult<ManagedUserResponse>> Create(CreateManagedUserRequest request, CancellationToken ct)
    {
        var user = await service.CreateAsync(request, ct);
        return Created($"/users/{user.Id}", user);
    }

    [HttpPut("{id:guid}")]
    public Task<ManagedUserResponse> Update(Guid id, UpdateManagedUserRequest request, CancellationToken ct) => service.UpdateAsync(id, request, ct);
}
