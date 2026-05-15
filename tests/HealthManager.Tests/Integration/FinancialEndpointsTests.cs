using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests.Integration;

public sealed class FinancialEndpointsTests
{
    [Fact]
    public async Task CreatePayment_ShouldPersistPaymentAndUpdateReceivable()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var response = await client.PostAsJsonAsync("/payments", new
        {
            receivableId = "ffffffff-ffff-ffff-ffff-ffffffffffff",
            amount = 50,
            paymentMethod = "Pix",
            paidAt = "2026-05-07T13:30:00Z",
            notes = "Pagamento parcial via teste de integracao"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await factory.WithDbContextAsync(async dbContext =>
        {
            var receivable = await dbContext.Receivables.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
            var payment = await dbContext.Payments.IgnoreQueryFilters().SingleAsync();

            receivable.ReceivedAmount.Should().Be(50);
            receivable.Status.Should().Be(ReceivableStatus.Partial);
            payment.Amount.Should().Be(50);
            payment.PaymentMethod.Should().Be(PaymentMethod.Pix);
        });
    }
}

