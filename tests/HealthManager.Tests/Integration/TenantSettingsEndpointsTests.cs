using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace HealthManager.Tests.Integration;

public sealed class TenantSettingsEndpointsTests
{
    [Fact]
    public async Task Admin_ShouldReadAndUpdateOwnClinicSettings()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var getResponse = await client.GetAsync("/tenant/settings");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updateResponse = await client.PutAsJsonAsync("/tenant/settings", new
        {
            name = "Clinica Aurora Atualizada",
            timezone = "America/Sao_Paulo",
            businessHoursJson = "{\"start\":\"09:00\",\"end\":\"17:00\"}",
            cnpj = "12345678000199",
            email = "contato@clinicaaurora.com",
            phone = "11999998888",
            address = "Avenida Central, 100"
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await updateResponse.Content.ReadFromJsonAsync<TenantSettingsBody>();
        body!.Name.Should().Be("Clinica Aurora Atualizada");
        body.Slug.Should().Be("clinica-aurora");
        body.BusinessHoursJson.Should().Be("{\"start\":\"09:00\",\"end\":\"17:00\"}");
    }

    [Fact]
    public async Task Doctor_ShouldReadSettings_ButNotUpdate()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("henrique.lima@clinicaaurora.com", "ChangeMe123!");

        var getResponse = await client.GetAsync("/tenant/settings");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updateResponse = await client.PutAsJsonAsync("/tenant/settings", new
        {
            name = "Tentativa de Update",
            timezone = "America/Sao_Paulo",
            businessHoursJson = "{\"start\":\"08:00\",\"end\":\"18:00\"}"
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Update_ShouldReturnBadRequest_WhenTimezoneIsInvalid()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var response = await client.PutAsJsonAsync("/tenant/settings", new
        {
            name = "Clinica Aurora",
            timezone = "Invalid/Timezone",
            businessHoursJson = "{\"start\":\"08:00\",\"end\":\"18:00\"}"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_ShouldReturnBadRequest_WhenBusinessHoursAreInvalid()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var response = await client.PutAsJsonAsync("/tenant/settings", new
        {
            name = "Clinica Aurora",
            timezone = "America/Sao_Paulo",
            businessHoursJson = "{\"start\":\"18:00\",\"end\":\"08:00\"}"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_ShouldNotChangeSlug()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var getBefore = await client.GetAsync("/tenant/settings");
        var bodyBefore = await getBefore.Content.ReadFromJsonAsync<TenantSettingsBody>();

        await client.PutAsJsonAsync("/tenant/settings", new
        {
            name = "Clinica Aurora Updated",
            timezone = "America/Sao_Paulo",
            businessHoursJson = "{\"start\":\"09:00\",\"end\":\"17:00\"}"
        });

        var getAfter = await client.GetAsync("/tenant/settings");
        var bodyAfter = await getAfter.Content.ReadFromJsonAsync<TenantSettingsBody>();

        bodyAfter!.Slug.Should().Be(bodyBefore!.Slug);
    }

    private sealed record TenantSettingsBody(string Name, string Slug, string BusinessHoursJson);
}
