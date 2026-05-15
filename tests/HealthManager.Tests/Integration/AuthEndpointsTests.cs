using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace HealthManager.Tests.Integration;

public sealed class AuthEndpointsTests
{
    [Fact]
    public async Task Login_ShouldReturnJwtAndUserData()
    {
        await using var factory = new ApiTestFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login", new
        {
            email = "admin@clinicaaurora.com",
            password = "ChangeMe123!"
        });
        var responseBody = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, responseBody);
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        root.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("refreshToken").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("user").GetProperty("email").GetString().Should().Be("admin@clinicaaurora.com");
        root.GetProperty("user").GetProperty("role").GetString().Should().Be("Admin");
    }
}
