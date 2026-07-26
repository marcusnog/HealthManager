using FluentAssertions;
using HealthManager.Application;
using HealthManager.Domain;

namespace HealthManager.Tests;

public sealed class PaymentGatewayTests
{
    [Fact]
    public async Task MockGatewayClient_ShouldAlwaysReturnConfirmed()
    {
        var client = new MockPaymentGatewayClient();
        var result = await client.GetPaymentStatusAsync("any-id", PaymentGatewayProvider.Asaas, Guid.NewGuid(), CancellationToken.None);

        result.Status.Should().Be(PaymentIntentStatus.Confirmed);
        result.FailureReason.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"status":"CONFIRMED","payment":"p-001"}""", null)]
    [InlineData("""{"payment":"p-001"}""", "Campo 'status' obrigatorio.")]
    [InlineData("""{"status":"CONFIRMED"}""", "Campo 'payment' obrigatorio.")]
    public void AsaasHandler_Validate_ShouldCheckRequiredFields(string payload, string? expectedError)
    {
        var handler = new AsaasGatewayHandler();
        var error = handler.Validate(payload);
        error.Should().Be(expectedError);
    }

    [Theory]
    [InlineData("""{"data":{"status":"approved","id":"123"}}""", null)]
    [InlineData("""{"data":{"id":"123"}}""", "Campo 'data.status' obrigatorio.")]
    [InlineData("""{"data":{"status":"approved"}}""", "Campo 'data.id' obrigatorio.")]
    public void MercadoPagoHandler_Validate_ShouldCheckRequiredFields(string payload, string? expectedError)
    {
        var handler = new MercadoPagoGatewayHandler();
        var error = handler.Validate(payload);
        error.Should().Be(expectedError);
    }

    [Theory]
    [InlineData("""{"type":"payment_intent.succeeded","data":{"object":{"id":"pi_123"}}}""", null)]
    [InlineData("""{"data":{"object":{"id":"pi_123"}}}""", "Campo 'type' obrigatorio.")]
    [InlineData("""{"type":"payment_intent.succeeded","data":{}}""", "Campo 'data.object' obrigatorio.")]
    public void StripeHandler_Validate_ShouldCheckRequiredFields(string payload, string? expectedError)
    {
        var handler = new StripeGatewayHandler();
        var error = handler.Validate(payload);
        error.Should().Be(expectedError);
    }
}
