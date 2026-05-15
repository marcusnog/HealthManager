using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests.Integration;

public sealed class AuthLifecycleEndpointsTests
{
    [Fact]
    public async Task Refresh_ShouldIssueNewTokens_AndLogoutShouldRevokeThem()
    {
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();
        var session = await factory.LoginWithSessionAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var refreshResponse = await client.PostAsJsonAsync("/auth/refresh", new
        {
            refreshToken = session.RefreshToken
        });

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshBody = await refreshResponse.Content.ReadAsStringAsync();
        using var refreshDocument = JsonDocument.Parse(refreshBody);
        var newAccessToken = refreshDocument.RootElement.GetProperty("accessToken").GetString();
        var newRefreshToken = refreshDocument.RootElement.GetProperty("refreshToken").GetString();

        newAccessToken.Should().NotBeNullOrWhiteSpace();
        newRefreshToken.Should().NotBeNullOrWhiteSpace();
        newRefreshToken.Should().NotBe(session.RefreshToken);

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", newAccessToken);

        var logoutResponse = await client.PostAsJsonAsync("/auth/logout", new
        {
            refreshToken = newRefreshToken
        });

        logoutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await factory.WithDbContextAsync(async dbContext =>
        {
            var revokedCount = await dbContext.RefreshTokens.IgnoreQueryFilters()
                .CountAsync(x => x.RevokedAt != null);

            revokedCount.Should().BeGreaterThanOrEqualTo(2);
        });
    }
}

