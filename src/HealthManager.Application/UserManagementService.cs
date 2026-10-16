using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Application;

public sealed record CreateManagedUserRequest([Required] string Name, [Required, EmailAddress] string Email,
    [Required, MinLength(8)] string Password, UserRole Role, string[]? Screens);
public sealed record UpdateManagedUserRequest([Required] string Name, [Required, EmailAddress] string Email,
    UserRole Role, bool IsActive, string[]? Screens);
public sealed record ManagedUserResponse(Guid Id, string Name, string Email, UserRole Role, bool IsActive, string[] Screens);
public sealed record AccessProfileResponse(UserRole Role, string[] AvailableScreens, string[] DefaultScreens);

public sealed class UserManagementService(IApplicationDbContext db, ITenantProvider tenant, IPasswordHasher hasher)
{
    public async Task<ManagedUserResponse[]> ListAsync(CancellationToken ct)
    {
        var clinicId = TenantGuard.RequireClinicId(tenant);
        var users = await db.Users.AsNoTracking().Where(x => x.ClinicId == clinicId && x.DeletedAt == null)
            .OrderBy(x => x.Name).ToListAsync(ct);
        return users.Select(Response).ToArray();
    }

    public async Task<ManagedUserResponse> CreateAsync(CreateManagedUserRequest request, CancellationToken ct)
    {
        var clinicId = TenantGuard.RequireClinicId(tenant);
        var screens = Validate(request.Name, request.Role, request.Screens);
        var email = await ValidateEmailAsync(request.Email, null, ct);
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            throw new InvalidOperationException("A senha deve ter pelo menos 8 caracteres.");
        var user = new User { ClinicId = clinicId, Name = request.Name.Trim(), Email = email,
            Role = request.Role, ScreenAccessJson = screens, PasswordHash = hasher.Hash(request.Password) };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return Response(user);
    }

    public async Task<ManagedUserResponse> UpdateAsync(Guid id, UpdateManagedUserRequest request, CancellationToken ct)
    {
        var clinicId = TenantGuard.RequireClinicId(tenant);
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == id && x.ClinicId == clinicId && x.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException("Usuario nao encontrado.");
        if (id == tenant.UserId && (!request.IsActive || request.Role != UserRole.Admin))
            throw new InvalidOperationException("Voce nao pode desativar ou retirar seu proprio acesso de administrador.");
        var screens = Validate(request.Name, request.Role, request.Screens);
        var email = await ValidateEmailAsync(request.Email, id, ct);
        user.Name = request.Name.Trim();
        user.Email = email;
        user.Role = request.Role;
        user.IsActive = request.IsActive;
        user.ScreenAccessJson = screens;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Response(user);
    }

    private static string? Validate(string name, UserRole role, string[]? screens)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Informe o nome.");
        if (role is not (UserRole.Admin or UserRole.Secretary or UserRole.Doctor))
            throw new InvalidOperationException("Perfil invalido.");
        if (screens is not null && screens.Except(ScreenAccess.Available(role)).Any())
            throw new InvalidOperationException("Tela nao permitida para este perfil.");
        return role == UserRole.Admin || screens is null ? null : JsonSerializer.Serialize(screens.Distinct().ToArray());
    }

    private async Task<string> ValidateEmailAsync(string email, Guid? id, CancellationToken ct)
    {
        email = email.Trim().ToLowerInvariant();
        if (!new EmailAddressAttribute().IsValid(email)) throw new InvalidOperationException("E-mail invalido.");
        if (await db.Users.IgnoreQueryFilters().AnyAsync(x => x.DeletedAt == null && x.Id != id && x.Email.ToLower() == email, ct))
            throw new InvalidOperationException("Este e-mail nao esta disponivel.");
        return email;
    }

    private static ManagedUserResponse Response(User user) => new(user.Id, user.Name, user.Email, user.Role, user.IsActive, ScreenAccess.ForUser(user));
}
