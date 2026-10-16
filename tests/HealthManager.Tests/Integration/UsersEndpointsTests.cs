using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests.Integration;

public sealed class UsersEndpointsTests
{
    [Fact]
    public async Task Admin_can_create_and_revoke_screens_for_an_existing_session()
    {
        await using var factory = new ApiTestFactory();
        var admin = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var created = await admin.PostAsJsonAsync("/users", new { name = "Recepcao", email = "reception@test.local", password = "Password123!", role = "Secretary", screens = new[] { "pacientes", "financeiro" } });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var session = await factory.LoginWithSessionAsync("RECEPTION@test.local", "Password123!");
        var user = factory.CreateClient();
        user.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        (await user.GetAsync("/patients")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await user.GetAsync("/financial/receivables")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await user.GetAsync("/dashboard/summary")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await user.GetAsync("/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await user.PostAsync("/hubs/payments/negotiate?negotiateVersion=1", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var update = new { name = "Recepcao", email = "reception@test.local", role = "Secretary", isActive = true, screens = Array.Empty<string>() };
        (await admin.PutAsJsonAsync($"/users/{id}", update)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await user.GetAsync("/patients")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await user.PostAsync("/hubs/payments/negotiate?negotiateVersion=1", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await user.GetAsync("/financial/receivables")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await user.PostAsJsonAsync("/checkout", new { receivableId = Guid.NewGuid(), paymentMethod = "Pix" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PutAsJsonAsync($"/users/{id}", new { update.name, update.email, update.role, isActive = true, screens = new[] { "dashboard", "financeiro-pagar" } })).EnsureSuccessStatusCode();
        var summary = await user.GetFromJsonAsync<JsonElement>("/dashboard/summary");
        summary.GetProperty("monthlyRevenue").GetDecimal().Should().Be(0);
        (await user.GetAsync("/financial/expenses")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await user.GetAsync("/expense-categories")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await user.PostAsJsonAsync("/expense-categories", new { name = "Forbidden" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PutAsJsonAsync($"/users/{id}", new { update.name, update.email, update.role, isActive = false, update.screens })).EnsureSuccessStatusCode();
        (await user.GetAsync("/patients")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await factory.CreateClient().PostAsJsonAsync("/auth/refresh", new { refreshToken = session.RefreshToken })).IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task Management_is_tenant_scoped_and_cannot_escalate_or_disable_self()
    {
        await using var factory = new ApiTestFactory();
        await factory.SeedSecondClinicPatientAsync();
        var otherId = Guid.NewGuid();
        await factory.WithDbContextAsync(async db => {
            db.Users.Add(new User { Id = otherId, ClinicId = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = "Other", Email = "other@test.local", PasswordHash = "unused" });
            await db.SaveChangesAsync();
        });
        var admin = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        admin.DefaultRequestHeaders.Add("X-Clinic-Id", "22222222-2222-2222-2222-222222222222");
        var list = await admin.GetStringAsync("/users");
        list.Should().NotContain("other@test.local").And.NotContain("passwordHash");
        var update = new { name = "Test", email = "test@test.local", role = "Secretary", isActive = false, screens = Array.Empty<string>() };
        (await admin.PutAsJsonAsync($"/users/{otherId}", update)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PutAsJsonAsync("/users/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", update)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        foreach (var role in new[] { "PlatformAdmin", "Patient", "999" })
            (await admin.PostAsJsonAsync("/users", new { name = "Test", email = "test@test.local", password = "Password123!", role })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/users", new { name = "Test", email = "OTHER@test.local", password = "Password123!", role = "Secretary" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/users", new { name = "Test", email = "test@test.local", password = "Password123!", role = "Secretary", screens = new[] { "usuarios" } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/users", new { name = "Test", email = "test@test.local", password = "short", role = "Secretary" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var doctor = await factory.CreateAuthenticatedClientAsync("henrique.lima@clinicaaurora.com", "ChangeMe123!");
        (await doctor.GetAsync("/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
