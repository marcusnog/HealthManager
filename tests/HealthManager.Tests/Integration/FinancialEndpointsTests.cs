using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests.Integration;

public sealed class FinancialEndpointsTests
{
    [Fact]
    public async Task ExpenseCategory_ShouldBeListedAndUsedWhenCreatingExpense()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var created = await client.PostAsJsonAsync("/expense-categories", new { name = "Laboratorio" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var category = await created.Content.ReadFromJsonAsync<CategoryDto>();

        var expense = await client.PostAsJsonAsync("/financial/expenses", new
        {
            description = "Exames ocupacionais", amount = 100, categoryId = category!.Id,
            paymentMethod = "Pix", status = "Paid"
        });

        expense.StatusCode.Should().Be(HttpStatusCode.Created);
        (await expense.Content.ReadFromJsonAsync<ExpenseDto>())!.CategoryName.Should().Be("Laboratorio");
    }

    [Fact]
    public async Task CreatePayment_ShouldPersistPaymentAndUpdateReceivable()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var response = await client.PostAsJsonAsync("/financial/payments", new
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

    private sealed record CategoryDto(Guid Id, string Name);
    private sealed record ExpenseDto(Guid Id, string CategoryName);
}

