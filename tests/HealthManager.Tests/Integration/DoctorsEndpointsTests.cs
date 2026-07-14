using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace HealthManager.Tests.Integration;

public sealed class DoctorsEndpointsTests
{
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
    }

    private sealed record DoctorHttpResponse(
        Guid Id,
        string Name,
        string Crm,
        string? Phone,
        string? Email,
        bool IsActive);
}
