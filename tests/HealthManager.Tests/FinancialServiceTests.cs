using FluentAssertions;
using HealthManager.Application;
using HealthManager.Domain;
using HealthManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests;

public sealed class FinancialServiceTests
{
    [Fact]
    public async Task CreatePaymentAsync_ShouldUpdateReceivableStatusToPartial()
    {
        var clinicId = Guid.NewGuid();
        var receivableId = Guid.NewGuid();

        await using var dbContext = CreateDbContext();
        dbContext.Clinics.Add(new Clinic { Id = clinicId, Name = "Clinica A", Slug = "clinica-a" });
        dbContext.Receivables.Add(new Receivable
        {
            Id = receivableId,
            ClinicId = clinicId,
            AppointmentId = Guid.NewGuid(),
            OriginalAmount = 200,
            ReceivedAmount = 0,
            DueDate = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var service = new FinancialService(dbContext, new FakeTenantProvider(clinicId));
        var response = await service.CreatePaymentAsync(
            new CreatePaymentRequest(receivableId, 50, PaymentMethod.Pix, DateTimeOffset.UtcNow, null),
            CancellationToken.None);

        response.Amount.Should().Be(50);
        dbContext.Receivables.Single().Status.Should().Be(ReceivableStatus.Partial);
        dbContext.Receivables.Single().ReceivedAmount.Should().Be(50);
    }

    private static AppDbContext CreateDbContext() => TestHelpers.CreateDbContext();
}
