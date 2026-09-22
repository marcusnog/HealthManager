using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests.Integration;

public sealed class CatalogEndpointsTests
{
    [Fact]
    public async Task ProductAndPackage_Workflow_ShouldPersistCompositionAndProtectProduct()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var productResponse = await client.PostAsJsonAsync("/products", new { name = "Vitamina D", price = 90m, applicationCount = 1 });
        productResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = await productResponse.Content.ReadFromJsonAsync<ProductHttpResponse>();

        var packageResponse = await client.PostAsJsonAsync("/packages", new
        {
            name = "Plano Vitamina D",
            price = 320m,
            items = new[] { new { productId = product!.Id, applicationCount = 4 } }
        });
        packageResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var package = await packageResponse.Content.ReadFromJsonAsync<PackageHttpResponse>();
        package!.Items.Should().ContainSingle(x => x.ProductId == product.Id && x.ApplicationCount == 4);

        (await client.DeleteAsync($"/products/{product.Id}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdatePackage_ShouldKeepExistingItemAndChangeApplicationCount()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var productResponse = await client.PostAsJsonAsync("/products", new { name = "Vitamina B12", price = 80m, applicationCount = 1 });
        var product = await productResponse.Content.ReadFromJsonAsync<ProductHttpResponse>();
        var packageResponse = await client.PostAsJsonAsync("/packages", new
        {
            name = "Plano B12",
            price = 300m,
            items = new[] { new { productId = product!.Id, applicationCount = 4 } }
        });
        var package = await packageResponse.Content.ReadFromJsonAsync<PackageHttpResponse>();
        var originalItemId = await factory.WithDbContextAsync(db => db.PackageItems
            .Where(x => x.PackageId == package!.Id && x.ProductId == product.Id)
            .Select(x => x.Id)
            .SingleAsync());

        var updateResponse = await client.PutAsJsonAsync($"/packages/{package!.Id}", new
        {
            name = "Plano B12 Atualizado",
            price = 350m,
            items = new[] { new { productId = product.Id, applicationCount = 6 } }
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<PackageHttpResponse>();
        updated!.Items.Should().ContainSingle(x => x.ProductId == product.Id && x.ApplicationCount == 6);
        await factory.WithDbContextAsync(async db =>
        {
            var item = await db.PackageItems.SingleAsync(x => x.PackageId == package.Id && x.ProductId == product.Id);
            item.Id.Should().Be(originalItemId);
            item.ApplicationCount.Should().Be(6);
        });
    }

    private sealed record ProductHttpResponse(Guid Id, string Name, decimal Price, int ApplicationCount, bool IsActive);
    private sealed record PackageItemHttpResponse(Guid ProductId, string ProductName, int ApplicationCount);
    private sealed record PackageHttpResponse(Guid Id, string Name, decimal Price, bool IsActive, List<PackageItemHttpResponse> Items);
}
