using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests.Integration;

public sealed class InternalClinicsEndpointsTests
{
    [Fact]
    public async Task PlatformAdmin_ShouldProvisionClinic_AndCreateClinicUser()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("platform@healthmanager.local", "ChangeMe123!");

        var createClinicResponse = await client.PostAsJsonAsync("/internal/clinics", new
        {
            name = "Clinica Horizonte",
            slug = "clinica-horizonte",
            timezone = "America/Sao_Paulo",
            businessHoursJson = "{\"start\":\"08:00\",\"end\":\"18:00\"}",
            cnpj = "12345678000199",
            email = "contato@clinicahorizonte.com",
            phone = "1130304040",
            address = "Rua das Palmeiras, 42 - Sao Paulo",
            adminName = "Mariana Costa",
            adminEmail = "admin@clinicahorizonte.com",
            adminPassword = "ChangeMe123!"
        });

        createClinicResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createClinicBody = await createClinicResponse.Content.ReadAsStringAsync();
        using var clinicDocument = JsonDocument.Parse(createClinicBody);
        var clinicId = clinicDocument.RootElement.GetProperty("clinicId").GetGuid();

        var createUserResponse = await client.PostAsJsonAsync($"/internal/clinics/{clinicId}/users", new
        {
            name = "Secretaria Horizonte",
            email = "secretaria@clinicahorizonte.com",
            password = "ChangeMe123!",
            role = "Secretary"
        });

        createUserResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        await factory.WithDbContextAsync(async dbContext =>
        {
            var clinic = await dbContext.Clinics.IgnoreQueryFilters().SingleAsync(x => x.Id == clinicId);
            var clinicAdmin = await dbContext.Users.IgnoreQueryFilters().SingleAsync(x => x.Email == "admin@clinicahorizonte.com");
            var secretary = await dbContext.Users.IgnoreQueryFilters().SingleAsync(x => x.Email == "secretaria@clinicahorizonte.com");

            clinic.Name.Should().Be("Clinica Horizonte");
            clinicAdmin.ClinicId.Should().Be(clinicId);
            clinicAdmin.Role.Should().Be(HealthManager.Domain.UserRole.Admin);
            secretary.ClinicId.Should().Be(clinicId);
            secretary.Role.Should().Be(HealthManager.Domain.UserRole.Secretary);
        });
    }
}
