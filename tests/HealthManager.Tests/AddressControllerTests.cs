using System.Net;
using System.Text;
using FluentAssertions;
using HealthManager.Api.Controllers;
using HealthManager.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace HealthManager.Tests;

public sealed class AddressControllerTests
{
    [Fact]
    public async Task FindByCep_ShouldMapViaCepResponse()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK,
            """{"cep":"01001-000","logradouro":"Praça da Sé","complemento":"lado ímpar","bairro":"Sé","localidade":"São Paulo","uf":"SP"}""");
        var controller = CreateController(handler);

        var result = await controller.FindByCep("01001-000", CancellationToken.None);

        var response = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        response.Value.Should().BeEquivalentTo(new CepAddressResponse(
            "01001000", "Praça da Sé", "lado ímpar", "Sé", "São Paulo", "SP"));
        handler.LastRequestUri.Should().Be("https://viacep.test/ws/01001000/json/");
    }

    [Fact]
    public async Task FindByCep_ShouldReturnNotFoundWhenViaCepReportsError()
    {
        var controller = CreateController(new StubHttpMessageHandler(HttpStatusCode.OK, """{"erro":true}"""));

        var result = await controller.FindByCep("99999999", CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task FindByCep_ShouldRejectInvalidCepWithoutCallingProvider()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "{}");
        var controller = CreateController(handler);

        var result = await controller.FindByCep("1234", CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        handler.LastRequestUri.Should().BeNull();
    }

    private static AddressController CreateController(StubHttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VIACEP_BASE_URL"] = "https://viacep.test/ws"
            })
            .Build();

        return new AddressController(new StubHttpClientFactory(handler), configuration);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHttpMessageHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public string? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
