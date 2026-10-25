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
            destinationBank = "Banco do Dono",
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
            payment.DestinationBank.Should().Be("Banco do Dono");
        });
    }

    [Fact]
    public async Task Summary_ShouldFilterRevenueByDestinationBank_AndSubtractPaidExpenses()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        await factory.WithDbContextAsync(async dbContext =>
        {
            var receivable = await dbContext.Receivables.SingleAsync(x => x.Id == Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
            receivable.ProfessionalId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
            receivable.ClinicSharePercentage = 30;
            await dbContext.SaveChangesAsync();
        });

        await client.PostAsJsonAsync("/financial/payments", new
        {
            receivableId = "ffffffff-ffff-ffff-ffff-ffffffffffff",
            amount = 50,
            paymentMethod = "Pix",
            destinationBank = "Banco do Dono",
            fundsRecipient = "Owner"
        });
        var categoryResponse = await client.PostAsJsonAsync("/expense-categories", new { name = "Abatimento" });
        var category = await categoryResponse.Content.ReadFromJsonAsync<CategoryDto>();
        await client.PostAsJsonAsync("/financial/expenses", new
        {
            description = "Valor devido pela clinica",
            amount = 10,
            categoryId = category!.Id,
            paymentMethod = "Pix",
            status = "Paid"
        });

        var summary = await client.GetFromJsonAsync<SummaryDto>("/financial/summary?destinationBank=Banco%20do%20Dono");

        summary.Should().Be(new SummaryDto(15, 10, 5, 50, 35, 50));

        (await client.PostAsJsonAsync("/financial/professional-settlements", new { professionalId = "cccccccc-cccc-cccc-cccc-cccccccccccc" })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/financial/owner-settlements", new { })).EnsureSuccessStatusCode();
        summary = await client.GetFromJsonAsync<SummaryDto>("/financial/summary?destinationBank=Banco%20do%20Dono");
        summary.Should().Be(new SummaryDto(15, 10, 5, 50, 0, 0));
    }

    [Fact]
    public async Task ProfessionalSettlement_ShouldSettleOnlySelectedPayments()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var professionalId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        await factory.WithDbContextAsync(async dbContext =>
        {
            var receivable = await dbContext.Receivables.SingleAsync(x => x.Id == Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
            receivable.ProfessionalId = professionalId;
            receivable.ClinicSharePercentage = 30;
            await dbContext.SaveChangesAsync();
        });
        await client.PostAsJsonAsync("/financial/payments", new { receivableId = "ffffffff-ffff-ffff-ffff-ffffffffffff", amount = 20, paymentMethod = "Pix" });
        await client.PostAsJsonAsync("/financial/payments", new { receivableId = "ffffffff-ffff-ffff-ffff-ffffffffffff", amount = 30, paymentMethod = "Pix" });

        var settlements = await client.GetFromJsonAsync<List<ProfessionalSettlementDto>>("/financial/professional-settlements");
        var items = settlements!.Single(x => x.ProfessionalId == professionalId).Items;
        items.Should().HaveCount(2);

        (await client.PostAsJsonAsync("/financial/professional-settlements", new { professionalId, paymentIds = new[] { items[0].PaymentId } })).EnsureSuccessStatusCode();

        settlements = await client.GetFromJsonAsync<List<ProfessionalSettlementDto>>("/financial/professional-settlements");
        settlements!.Single(x => x.ProfessionalId == professionalId).Items.Should().ContainSingle().Which.PaymentId.Should().Be(items[1].PaymentId);
        var settled = settlements!.Single(x => x.ProfessionalId == professionalId).SettledItems.Should().ContainSingle().Subject;
        settled.PaymentId.Should().Be(items[0].PaymentId);
        settled.Amount.Should().Be(items[0].Amount);
        settled.ProfessionalPaidAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ManualPayment_ShouldLinkPatientWithoutAppointment()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var patientId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var response = await client.PostAsJsonAsync("/financial/receivables/manual", new { patientId, amount = 75, paymentMethod = "Pix" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<ManualReceivableDto>();
        created!.PatientId.Should().Be(patientId);
        created.AppointmentId.Should().BeNull();
        created.OutstandingAmount.Should().Be(0);
        await factory.WithDbContextAsync(async db =>
        {
            var payment = await db.Payments.SingleAsync();
            payment.ClinicRevenueAmount.Should().Be(75);
            payment.ProfessionalPayableAmount.Should().Be(0);
            (await db.Receivables.SingleAsync(r => r.Id == created.Id)).PatientId.Should().Be(patientId);
        });
        var payments = await client.GetFromJsonAsync<HealthManager.Application.PagedResult<ManualPaymentDto>>("/financial/payments");
        payments!.Items.Single().PatientName.Should().Be(created.PatientName);
        var receivables = await client.GetFromJsonAsync<HealthManager.Application.PagedResult<ManualReceivableDto>>("/financial/receivables");
        receivables!.Items.Single(r => r.Id == created.Id).PatientId.Should().Be(patientId);
    }

    [Fact]
    public async Task ManualPayment_ShouldRejectPatientFromAnotherClinic()
    {
        await using var factory = new ApiTestFactory();
        await factory.SeedSecondClinicPatientAsync();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var response = await client.PostAsJsonAsync("/financial/receivables/manual", new { patientId = "99999999-9999-9999-9999-999999999999", amount = 75, paymentMethod = "Pix" });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await factory.WithDbContextAsync(async db => (await db.Payments.CountAsync()).Should().Be(0));
    }

    [Fact]
    public async Task Portal_ShouldIncludeOwnManualPaymentAndExcludeOtherPatients()
    {
        await using var factory = new ApiTestFactory();
        using var admin = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var patientId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var response = await admin.PostAsJsonAsync("/financial/receivables/manual", new { patientId, amount = 75, paymentMethod = "Pix" });
        response.EnsureSuccessStatusCode();
        var own = (await response.Content.ReadFromJsonAsync<ManualReceivableDto>())!;
        string cpf = "", token = "";
        Guid otherReceiptId = Guid.NewGuid();
        await factory.WithDbContextAsync(async db =>
        {
            var patient = await db.Patients.SingleAsync(x => x.Id == patientId);
            cpf = patient.Cpf;
            token = patient.PatientAccessToken.ToString();
            var other = new Patient { ClinicId = patient.ClinicId, Name = "Outro paciente", Cpf = "", Phone = "" };
            db.Patients.Add(other);
            db.Receivables.Add(new Receivable { Id = otherReceiptId, ClinicId = patient.ClinicId, PatientId = other.Id, OriginalAmount = 99 });
            await db.SaveChangesAsync();
        });
        using var portal = factory.CreateClient();
        var login = await portal.PostAsJsonAsync("/portal/auth", new { cpf, accessToken = token });
        login.EnsureSuccessStatusCode();
        var auth = await login.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        portal.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        var receipts = await portal.GetFromJsonAsync<List<ManualReceivableDto>>("/portal/receivables");
        receipts!.Should().Contain(x => x.Id == own.Id);
        receipts.Should().Contain(x => x.Id == Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        receipts.Should().NotContain(x => x.Id == otherReceiptId);
    }

    private sealed record ManualReceivableDto(Guid Id, Guid? PatientId, string? PatientName, Guid? AppointmentId, decimal OutstandingAmount);
    private sealed record ManualPaymentDto(string? PatientName);
    private sealed record CategoryDto(Guid Id, string Name);
    private sealed record ExpenseDto(Guid Id, string CategoryName);
    private sealed record SummaryDto(decimal TotalReceived, decimal TotalExpenses, decimal Balance, decimal GrossReceived, decimal ProfessionalLiability, decimal OwnerReceivable);
    private sealed record ProfessionalSettlementDto(Guid ProfessionalId, List<ProfessionalSettlementItemDto> Items, List<ProfessionalSettlementItemDto> SettledItems);
    private sealed record ProfessionalSettlementItemDto(Guid PaymentId, decimal Amount, DateTimeOffset? ProfessionalPaidAt);
}

