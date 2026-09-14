using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests.Integration;

public sealed class FinancialPermissionsTests
{
    private async Task SeedSecretaryUserAsync(ApiTestFactory factory)
    {
        await factory.WithDbContextAsync(async dbContext =>
        {
            var admin = await dbContext.Users.SingleAsync(x => x.Email == "admin@clinicaaurora.com");
            dbContext.Users.Add(new User
            {
                ClinicId = admin.ClinicId,
                Name = "Recepcionista",
                Email = "recepcao@clinicaaurora.com",
                PasswordHash = admin.PasswordHash,
                Role = UserRole.Secretary
            });
            await dbContext.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task LoginResponse_ShouldExposeGranularPermissions()
    {
        await using var factory = new ApiTestFactory();
        var session = await factory.LoginWithSessionAsync("admin@clinicaaurora.com", "ChangeMe123!");

        session.Role.Should().Be(UserRole.Admin.ToString());

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessToken);
        var me = await (await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = session.RefreshToken }))
            .Content.ReadFromJsonAsync<AuthResponseDto>();
        me!.User.Permissions.Should().Contain(Permissions.FinanceCategoriesView);
        me.User.Permissions.Should().Contain(Permissions.FinanceSettlements);
    }

    [Fact]
    public async Task Doctor_ShouldViewReceivables_ButNotManagePayments()
    {
        await using var factory = new ApiTestFactory();
        using var doctorClient = await factory.CreateAuthenticatedClientAsync("henrique.lima@clinicaaurora.com", "ChangeMe123!");

        var list = await doctorClient.GetAsync("/financial/receivables");
        list.StatusCode.Should().Be(HttpStatusCode.OK);

        var create = await doctorClient.PostAsJsonAsync("/financial/payments", new
        {
            receivableId = "ffffffff-ffff-ffff-ffff-ffffffffffff",
            amount = 50,
            paymentMethod = "Pix"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Doctor_ShouldViewSummary_ButNotExpensesOrCategories()
    {
        await using var factory = new ApiTestFactory();
        using var doctorClient = await factory.CreateAuthenticatedClientAsync("henrique.lima@clinicaaurora.com", "ChangeMe123!");

        (await doctorClient.GetAsync("/financial/summary")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await doctorClient.GetAsync("/financial/expenses")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await doctorClient.GetAsync("/expense-categories")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Secretary_ShouldViewAndCreatePayments_ButNotManageExpenses()
    {
        await using var factory = new ApiTestFactory();
        await SeedSecretaryUserAsync(factory);
        using var secretaryClient = await factory.CreateAuthenticatedClientAsync("recepcao@clinicaaurora.com", "ChangeMe123!");

        var list = await secretaryClient.GetAsync("/financial/receivables");
        list.StatusCode.Should().Be(HttpStatusCode.OK);

        var create = await secretaryClient.PostAsJsonAsync("/financial/payments", new
        {
            receivableId = "ffffffff-ffff-ffff-ffff-ffffffffffff",
            amount = 50,
            paymentMethod = "Pix"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        (await secretaryClient.GetAsync("/financial/expenses")).StatusCode.Should().Be(HttpStatusCode.OK);

        var createExpense = await secretaryClient.PostAsJsonAsync("/financial/expenses", new
        {
            description = "Tentativa sem permissao",
            amount = 10,
            categoryId = "ca000001-0000-0000-0000-000000000001",
            paymentMethod = "Pix"
        });
        createExpense.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await secretaryClient.GetAsync("/financial/professional-settlements")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await secretaryClient.GetAsync("/expense-categories")).StatusCode.Should().Be(HttpStatusCode.OK);

        var createCategory = await secretaryClient.PostAsJsonAsync("/expense-categories", new { name = "Transporte" });
        createCategory.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Admin_ShouldManageEveryFinancialScreen()
    {
        await using var factory = new ApiTestFactory();
        using var adminClient = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        (await adminClient.GetAsync("/financial/expenses")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await adminClient.GetAsync("/financial/professional-settlements")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await adminClient.GetAsync("/expense-categories")).StatusCode.Should().Be(HttpStatusCode.OK);

        var createExpense = await adminClient.PostAsJsonAsync("/financial/expenses", new
        {
            description = "Material",
            amount = 10,
            categoryId = "ca000001-0000-0000-0000-000000000001",
            paymentMethod = "Pix"
        });
        createExpense.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private sealed record AuthResponseDto(UserResponseDto User);
    private sealed record UserResponseDto(string Email, string Role, string[] Permissions);
}