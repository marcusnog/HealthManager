using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests.Integration;

public sealed class PatientApplicationsEndpointsTests
{
    private static readonly Guid PatientId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [Fact]
    public async Task SaleGrantAndApplicationFlow_ShouldTrackPackageAndIndividualBalances()
    {
        await using var factory = new ApiTestFactory();
        using var admin = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        using var doctor = await factory.CreateAuthenticatedClientAsync("henrique.lima@clinicaaurora.com", "ChangeMe123!");

        var productResponse = await admin.PostAsJsonAsync("/products", new { name = "Vitamina D", price = 90m, applicationCount = 1 });
        productResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = await productResponse.Content.ReadFromJsonAsync<ProductDto>();
        var productId = product!.Id;

        var packageResponse = await admin.PostAsJsonAsync("/packages", new
        {
            name = "Plano Vitamina D",
            price = 320m,
            items = new[] { new { productId, applicationCount = 4 } }
        });
        var package = await packageResponse.Content.ReadFromJsonAsync<PackageDto>();

        var grant = await admin.PostAsJsonAsync($"/patients/{PatientId}/applications/balance", new { source = "package", packageId = package!.Id });
        grant.StatusCode.Should().Be(HttpStatusCode.Created);
        var balances = await grant.Content.ReadFromJsonAsync<List<BalanceDto>>();
        balances!.Should().ContainSingle(x => x.ProductId == productId && x.Source == "Package" && x.PurchasedUnits == 4 && x.RemainingUnits == 4);

        var apply = await doctor.PostAsJsonAsync($"/patients/{PatientId}/applications", new { productId, quantity = 1 });
        apply.StatusCode.Should().Be(HttpStatusCode.Created);
        await apply.Content.ReadFromJsonAsync<ApplicationDto>();

        (await admin.GetFromJsonAsync<List<BalanceDto>>($"/patients/{PatientId}/applications/balance"))!
            .Single(x => x.Source == "Package").RemainingUnits.Should().Be(3);

        var insufficient = await doctor.PostAsJsonAsync($"/patients/{PatientId}/applications", new { productId, quantity = 5 });
        insufficient.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var productSale = await admin.PostAsJsonAsync($"/patients/{PatientId}/applications/balance", new { source = "individual", productId });
        var balancesAfterSale = await productSale.Content.ReadFromJsonAsync<List<BalanceDto>>();
        var individualLine = balancesAfterSale!.Single(x => x.Source == "Individual");
        individualLine.PurchasedUnits.Should().Be(1);

        var targeted = await doctor.PostAsJsonAsync($"/patients/{PatientId}/applications", new { productId, quantity = 1, balanceId = individualLine.Id });
        targeted.StatusCode.Should().Be(HttpStatusCode.Created);

        (await admin.GetFromJsonAsync<List<BalanceDto>>($"/patients/{PatientId}/applications/balance"))!
            .Single(x => x.Source == "Individual").RemainingUnits.Should().Be(0);

        var sessions = await doctor.GetFromJsonAsync<List<ApplicationDto>>($"/patients/{PatientId}/applications");
        sessions!.Count.Should().Be(2);

        var denied = await doctor.PostAsJsonAsync($"/patients/{PatientId}/applications/balance", new { source = "individual", productId });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ConcurrentApplications_ShouldNeverExceedGrantedBalance()
    {
        await using var factory = new ApiTestFactory();
        using var admin = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        using var doctor = await factory.CreateAuthenticatedClientAsync("henrique.lima@clinicaaurora.com", "ChangeMe123!");

        var productResponse = await admin.PostAsJsonAsync("/products", new { name = "Soro facial", price = 120m, applicationCount = 1 });
        productResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = (await productResponse.Content.ReadFromJsonAsync<ProductDto>())!;

        var sale = await admin.PostAsJsonAsync($"/patients/{PatientId}/applications/balance", new { source = "individual", productId = product.Id });
        sale.StatusCode.Should().Be(HttpStatusCode.Created);

        var attempts = Enumerable.Range(0, 5)
            .Select(_ => doctor.PostAsJsonAsync($"/patients/{PatientId}/applications", new { productId = product.Id, quantity = 1 }))
            .ToList();
        var statuses = await Task.WhenAll(attempts.Select(async a => (await a).StatusCode));

        statuses.Count(x => x == HttpStatusCode.Created).Should().Be(1);
        statuses.Count(x => x == HttpStatusCode.BadRequest).Should().Be(4);

        var balances = await admin.GetFromJsonAsync<List<BalanceDto>>($"/patients/{PatientId}/applications/balance");
        balances!.Single(x => x.Source == "Individual").RemainingUnits.Should().Be(0);

        var applications = await doctor.GetFromJsonAsync<List<ApplicationDto>>($"/patients/{PatientId}/applications");
        applications!.Count(x => x.ProductId == product.Id).Should().Be(1);
    }

    [Fact]
    public async Task SecretaryCannotRegisterApplication()
    {
        await using var factory = new ApiTestFactory();

        await factory.WithDbContextAsync(async dbContext =>
        {
            var doctor = await dbContext.Users.SingleAsync(x => x.Email == "henrique.lima@clinicaaurora.com");
            dbContext.Users.Add(new User
            {
                ClinicId = doctor.ClinicId,
                Name = "Secretaria Aurora",
                Email = "secretaria@clinicaaurora.com",
                PasswordHash = doctor.PasswordHash,
                Role = UserRole.Secretary
            });
            await dbContext.SaveChangesAsync();
        });

        using var admin = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        using var secretary = await factory.CreateAuthenticatedClientAsync("secretaria@clinicaaurora.com", "ChangeMe123!");

        var productResponse = await admin.PostAsJsonAsync("/products", new { name = "Alinhador", price = 80m, applicationCount = 1 });
        var product = (await productResponse.Content.ReadFromJsonAsync<ProductDto>())!;
        await admin.PostAsJsonAsync($"/patients/{PatientId}/applications/balance", new { source = "individual", productId = product.Id });

        var apply = await secretary.PostAsJsonAsync($"/patients/{PatientId}/applications", new { productId = product.Id, quantity = 1 });
        apply.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed record ProductDto(Guid Id, string Name, decimal Price, int ApplicationCount, bool IsActive);
    private sealed record PackageItemDto(Guid ProductId, string ProductName, int ApplicationCount);
    private sealed record PackageDto(Guid Id, string Name, decimal Price, bool IsActive, List<PackageItemDto> Items);
    private sealed record BalanceDto(Guid Id, Guid ProductId, string ProductName, string Source, Guid? PackageId, string? PackageName, int PurchasedUnits, int UsedUnits, int RemainingUnits);
    private sealed record ApplicationDto(Guid Id, Guid ProductId, string ProductName, int Quantity, DateTimeOffset AppliedAt, Guid? DoctorId, string? DoctorName, string? Notes);
}