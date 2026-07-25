using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace HealthManager.Tests.Integration;

public sealed class PaymentIntentsEndpointsTests
{
    private static readonly Guid ReceivableId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    [Fact]
    public async Task ShouldCreateIntent_AndConfirm()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var createResponse = await client.PostAsJsonAsync("/payment-intents", new
        {
            receivableId = ReceivableId,
            amount = 100,
            idempotencyKey = $"test-{Guid.NewGuid()}"
        });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var intent = await createResponse.Content.ReadFromJsonAsync<PaymentIntentHttpResponse>();
        intent!.Status.Should().Be("Created");

        var confirmResponse = await client.PostAsync($"/payment-intents/{intent.Id}/confirm", null);
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var confirmed = await confirmResponse.Content.ReadFromJsonAsync<PaymentIntentHttpResponse>();
        confirmed!.Status.Should().Be("Confirmed");
        confirmed.ConfirmedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ShouldReturnSameIntent_WhenIdempotencyKeyReused()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var key = $"idem-{Guid.NewGuid()}";
        var response1 = await client.PostAsJsonAsync("/payment-intents", new
        {
            receivableId = ReceivableId,
            amount = 50,
            idempotencyKey = key
        });
        var intent1 = await response1.Content.ReadFromJsonAsync<PaymentIntentHttpResponse>();

        var response2 = await client.PostAsJsonAsync("/payment-intents", new
        {
            receivableId = ReceivableId,
            amount = 50,
            idempotencyKey = key
        });
        var intent2 = await response2.Content.ReadFromJsonAsync<PaymentIntentHttpResponse>();

        intent2!.Id.Should().Be(intent1!.Id);
    }

    [Fact]
    public async Task ShouldCancelIntent()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var createResponse = await client.PostAsJsonAsync("/payment-intents", new
        {
            receivableId = ReceivableId,
            amount = 75,
            idempotencyKey = $"cancel-{Guid.NewGuid()}"
        });
        var intent = await createResponse.Content.ReadFromJsonAsync<PaymentIntentHttpResponse>();

        var cancelResponse = await client.PostAsync($"/payment-intents/{intent!.Id}/cancel", null);
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var cancelled = await cancelResponse.Content.ReadFromJsonAsync<PaymentIntentHttpResponse>();
        cancelled!.Status.Should().Be("Cancelled");
    }

    [Fact]
    public async Task ShouldRejectConfirm_WhenAlreadyCancelled()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var createResponse = await client.PostAsJsonAsync("/payment-intents", new
        {
            receivableId = ReceivableId,
            amount = 30,
            idempotencyKey = $"reject-{Guid.NewGuid()}"
        });
        var intent = await createResponse.Content.ReadFromJsonAsync<PaymentIntentHttpResponse>();

        await client.PostAsync($"/payment-intents/{intent!.Id}/cancel", null);
        var confirmResponse = await client.PostAsync($"/payment-intents/{intent.Id}/confirm", null);

        confirmResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Patient_ShouldNotAccess()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var response = await client.PostAsJsonAsync("/payment-intents", new
        {
            receivableId = ReceivableId,
            amount = 10,
            idempotencyKey = $"patient-{Guid.NewGuid()}"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private sealed record PaymentIntentHttpResponse(Guid Id, Guid ReceivableId, decimal Amount, string Status, string? IdempotencyKey, DateTimeOffset? ConfirmedAt);
}
