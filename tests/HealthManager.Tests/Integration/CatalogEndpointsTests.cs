using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

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

    private sealed record ProductHttpResponse(Guid Id, string Name, decimal Price, int ApplicationCount, bool IsActive);
    private sealed record PackageItemHttpResponse(Guid ProductId, string ProductName, int ApplicationCount);
    private sealed record PackageHttpResponse(Guid Id, string Name, decimal Price, bool IsActive, List<PackageItemHttpResponse> Items);
}
