using System.Net;
using FluentAssertions;
using HealthManager.Infrastructure.Integrations;

namespace HealthManager.Tests;

public sealed class MetaCloudApiClientTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"messages\":[]}")]
    [InlineData("{\"messages\":[{\"id\":null}]}")]
    [InlineData("{\"messages\":false}")]
    public async Task MalformedSuccess_IsUncertainInsteadOfReportedAsSent(string body)
    {
        using var handler = new ResponseHandler(HttpStatusCode.OK, body);
        using var http = new HttpClient(handler);
        var client = new MetaCloudApiClient(new ClientFactory(http));
        var result = await client.SendTextAsync("number", "test-token", "5511999998888", "Olá", default);
        result.Success.Should().BeFalse();
        result.DeliveryUncertain.Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    public async Task ProviderErrors_AreSanitized(HttpStatusCode status, bool uncertain)
    {
        using var handler = new ResponseHandler(status, "sensitive-provider-content");
        using var http = new HttpClient(handler);
        var result = await new MetaCloudApiClient(new ClientFactory(http)).SendTextAsync("number", "test-token", "5511999998888", "Olá", default);
        result.Error.Should().NotContain("sensitive-provider-content");
        result.DeliveryUncertain.Should().Be(uncertain);
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
    private sealed class ResponseHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
