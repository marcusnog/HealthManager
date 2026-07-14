using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace HealthManager.Tests.Integration;

public sealed class DoctorsEndpointsTests
{
    [Fact]
    public async Task CreateDoctor_WithExistingCrm_ShouldReturnClearValidationError()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var response = await client.PostAsJsonAsync("/doctors", new
        {
            name = "Outro medico",
            crm = "CRM-SP-123456",
            specialtyIds = Array.Empty<Guid>()
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemHttpResponse>();
        problem!.Detail.Should().Be("CRM ja cadastrado para esta clinica.");
    }

    [Fact]
    public async Task CreateDoctor_WithSpecialty_ShouldPersistDoctorAndLink()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var specialtyId = Guid.Parse("d1d2d3d4-d1d2-d1d2-d1d2-d1d2d3d4d5d6");

        var response = await client.PostAsJsonAsync("/doctors", new
        {
            name = "Dra. Maria Silva",
            crm = "CRM-SP-654321",
            phone = "11988887777",
            specialtyIds = new[] { specialtyId }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var payload = await response.Content.ReadFromJsonAsync<DoctorHttpResponse>();
        payload.Should().NotBeNull();
        payload!.Specialties.Should().ContainSingle(x => x.Id == specialtyId);
    }

    [Fact]
    public async Task UpdateDoctor_WithoutPreviousLink_ShouldAddSpecialty()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var specialtyId = Guid.Parse("d1d2d3d4-d1d2-d1d2-d1d2-d1d2d3d4d5d6");

        var createdResponse = await client.PostAsJsonAsync("/doctors", new
        {
            name = "Dr. Sem Especialidade",
            crm = "CRM-SP-777777",
            specialtyIds = Array.Empty<Guid>()
        });
        createdResponse.EnsureSuccessStatusCode();
        var created = await createdResponse.Content.ReadFromJsonAsync<DoctorHttpResponse>();

        var updateResponse = await client.PutAsJsonAsync($"/doctors/{created!.Id}", new
        {
            name = created.Name,
            phone = created.Phone,
            email = created.Email,
            isActive = true,
            specialtyIds = new[] { specialtyId }
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<DoctorHttpResponse>();
        updated!.Specialties.Should().ContainSingle(x => x.Id == specialtyId);
    }

    [Fact]
    public async Task UpdateDoctor_ShouldPersistChangesForCurrentClinicDoctor()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var doctorId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        var updateResponse = await client.PutAsJsonAsync($"/doctors/{doctorId}", new
        {
            name = "Dr. Carlos Eduardo",
            phone = "11994443322",
            email = "carlos.eduardo@clinicaaurora.com",
            isActive = true,
            specialtyIds = new Guid[] { }
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await updateResponse.Content.ReadFromJsonAsync<DoctorHttpResponse>();

        payload.Should().NotBeNull();
        payload!.Name.Should().Be("Dr. Carlos Eduardo");
        payload.Phone.Should().Be("11994443322");
        payload.Email.Should().Be("carlos.eduardo@clinicaaurora.com");
        payload.IsActive.Should().BeTrue();
        payload.Specialties.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateDoctor_ShouldRemoveAndRestoreSpecialtyWithoutDeletingLink()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var doctorId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var specialtyId = Guid.Parse("d1d2d3d4-d1d2-d1d2-d1d2-d1d2d3d4d5d6");

        var withoutSpecialty = await client.PutAsJsonAsync($"/doctors/{doctorId}", new
        {
            name = "Dr. Henrique Lima",
            phone = "11998887766",
            email = "henrique.lima@clinicaaurora.com",
            isActive = true,
            specialtyIds = Array.Empty<Guid>()
        });
        withoutSpecialty.StatusCode.Should().Be(HttpStatusCode.OK);

        var restored = await client.PutAsJsonAsync($"/doctors/{doctorId}", new
        {
            name = "Dr. Henrique Lima",
            phone = "11998887766",
            email = "henrique.lima@clinicaaurora.com",
            isActive = true,
            specialtyIds = new[] { specialtyId }
        });

        restored.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await restored.Content.ReadFromJsonAsync<DoctorHttpResponse>();
        payload!.Specialties.Should().ContainSingle(x => x.Id == specialtyId);
    }

    private sealed record DoctorHttpResponse(
        Guid Id,
        string Name,
        string Crm,
        string? Phone,
        string? Email,
        bool IsActive,
        List<SpecialtyHttpResponse> Specialties);

    private sealed record SpecialtyHttpResponse(Guid Id, string Name);
    private sealed record ProblemHttpResponse(string Detail);
}
