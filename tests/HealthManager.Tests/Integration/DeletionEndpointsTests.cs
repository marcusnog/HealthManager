using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests.Integration;

public sealed class DeletionEndpointsTests
{
    private static readonly Guid PatientId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid ReceivableId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    [Theory]
    [InlineData(UserRole.Secretary)]
    [InlineData(UserRole.Doctor)]
    public async Task NonAdmin_CannotDeletePatient(UserRole role)
    {
        await using var factory = new ApiTestFactory();
        await factory.WithDbContextAsync(async db =>
        {
            (await db.Users.SingleAsync(x => x.Email == "admin@clinicaaurora.com")).Role = role;
            await db.SaveChangesAsync();
        });
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        (await client.DeleteAsync($"/patients/{PatientId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(false, 0, ReceivableStatus.Pending, HttpStatusCode.BadRequest)]
    [InlineData(false, 50, ReceivableStatus.Partial, HttpStatusCode.BadRequest)]
    [InlineData(true, 0, ReceivableStatus.Pending, HttpStatusCode.BadRequest)]
    [InlineData(true, 50, ReceivableStatus.Partial, HttpStatusCode.BadRequest)]
    [InlineData(false, 0, ReceivableStatus.Cancelled, HttpStatusCode.NoContent)]
    [InlineData(false, 100, ReceivableStatus.Paid, HttpStatusCode.NoContent)]
    public async Task PatientDeletion_ProtectsOutstandingBalances(bool manual, int received, ReceivableStatus status, HttpStatusCode expected)
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        await factory.WithDbContextAsync(async db =>
        {
            var receivable = await db.Receivables.SingleAsync(x => x.Id == ReceivableId);
            receivable.OriginalAmount = 100;
            receivable.ReceivedAmount = received;
            receivable.Status = status;
            receivable.DueDate = DateTimeOffset.UtcNow.AddDays(-10);
            if (manual) { receivable.AppointmentId = null; receivable.PatientId = PatientId; }
            await db.SaveChangesAsync();
        });
        var response = await client.DeleteAsync($"/patients/{PatientId}");
        response.StatusCode.Should().Be(expected);
        await factory.WithDbContextAsync(async db =>
        {
            var patient = await db.Patients.IgnoreQueryFilters().SingleAsync(x => x.Id == PatientId);
            (patient.DeletedAt != null).Should().Be(expected == HttpStatusCode.NoContent);
            (await db.AuditLogs.AnyAsync(x => x.Action == "patient.deleted")).Should().Be(expected == HttpStatusCode.NoContent);
        });
    }

    [Theory]
    [InlineData(0, false, ReceivableStatus.Pending)]
    [InlineData(50, false, ReceivableStatus.Partial)]
    [InlineData(0, true, ReceivableStatus.Cancelled)]
    public async Task PaymentDeletion_ReopensBalanceAndAuditsOnce(int remaining, bool cancelled, ReceivableStatus expected)
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var paymentId = await SeedPayment(factory, remaining, cancelled);
        (await client.DeleteAsync($"/financial/payments/{paymentId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/financial/payments/{paymentId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await factory.WithDbContextAsync(async db =>
        {
            var receivable = await db.Receivables.SingleAsync(x => x.Id == ReceivableId);
            receivable.ReceivedAmount.Should().Be(remaining);
            receivable.Status.Should().Be(expected);
            (await db.Payments.AnyAsync(x => x.Id == paymentId)).Should().BeFalse();
            (await db.Payments.IgnoreQueryFilters().SingleAsync(x => x.Id == paymentId)).DeletedAt.Should().NotBeNull();
            (await db.AuditLogs.CountAsync(x => x.Action == "payment.deleted" && x.EntityId == paymentId)).Should().Be(1);
        });
    }

    [Theory]
    [InlineData("professional", HttpStatusCode.BadRequest)]
    [InlineData("owner", HttpStatusCode.BadRequest)]
    [InlineData("other-clinic", HttpStatusCode.NotFound)]
    [InlineData("doctor", HttpStatusCode.Forbidden)]
    public async Task PaymentDeletion_ProtectsSettlementsPermissionsAndTenant(string protection, HttpStatusCode expected)
    {
        await using var factory = new ApiTestFactory();
        var paymentId = await SeedPayment(factory);
        await factory.WithDbContextAsync(async db =>
        {
            var payment = await db.Payments.SingleAsync(x => x.Id == paymentId);
            if (protection == "professional") payment.ProfessionalPaidAt = DateTimeOffset.UtcNow;
            if (protection == "owner") payment.OwnerSettledAt = DateTimeOffset.UtcNow;
            if (protection == "other-clinic") payment.ClinicId = Guid.NewGuid();
            await db.SaveChangesAsync();
        });
        using var client = await factory.CreateAuthenticatedClientAsync(protection == "doctor" ? "henrique.lima@clinicaaurora.com" : "admin@clinicaaurora.com", "ChangeMe123!");
        (await client.DeleteAsync($"/financial/payments/{paymentId}")).StatusCode.Should().Be(expected);
        await factory.WithDbContextAsync(async db =>
        {
            (await db.Payments.IgnoreQueryFilters().SingleAsync(x => x.Id == paymentId)).DeletedAt.Should().BeNull();
            (await db.Receivables.SingleAsync(x => x.Id == ReceivableId)).ReceivedAmount.Should().Be(100);
        });
    }

    [Theory]
    [InlineData("balance")]
    [InlineData("cancelled")]
    [InlineData("amount")]
    [InlineData("professional")]
    [InlineData("owner")]
    [InlineData("deleted")]
    public async Task ConcurrentPaymentChanges_CannotOverwriteTheFirstWriter(string change)
    {
        await using var factory = new ApiTestFactory();
        var paymentId = await SeedPayment(factory);
        await factory.WithDbContextAsync(async stale =>
        {
            var payment = await stale.Payments.SingleAsync(x => x.Id == paymentId);
            var receivable = await stale.Receivables.SingleAsync(x => x.Id == ReceivableId);
            await factory.WithDbContextAsync(async current =>
            {
                var freshPayment = await current.Payments.SingleAsync(x => x.Id == paymentId);
                if (change == "balance") (await current.Receivables.SingleAsync(x => x.Id == ReceivableId)).ReceivedAmount = 90;
                if (change == "cancelled") (await current.Receivables.SingleAsync(x => x.Id == ReceivableId)).Status = ReceivableStatus.Cancelled;
                if (change == "amount") (await current.Receivables.SingleAsync(x => x.Id == ReceivableId)).OriginalAmount = 110;
                if (change == "professional") freshPayment.ProfessionalPaidAt = DateTimeOffset.UtcNow;
                if (change == "owner") freshPayment.OwnerSettledAt = DateTimeOffset.UtcNow;
                if (change == "deleted") freshPayment.DeletedAt = DateTimeOffset.UtcNow;
                await current.SaveChangesAsync();
            });
            if (change is "balance" or "cancelled" or "amount") receivable.ReceivedAmount = 0;
            else payment.DeletedAt = DateTimeOffset.UtcNow;
            var save = () => stale.SaveChangesAsync();
            await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
        });
    }

    private static async Task<Guid> SeedPayment(ApiTestFactory factory, int remaining = 0, bool cancelled = false)
    {
        var id = Guid.NewGuid();
        await factory.WithDbContextAsync(async db =>
        {
            var receivable = await db.Receivables.SingleAsync(x => x.Id == ReceivableId);
            receivable.OriginalAmount = receivable.ReceivedAmount = 100;
            receivable.Status = cancelled ? ReceivableStatus.Cancelled : ReceivableStatus.Paid;
            db.Payments.Add(new Payment { Id = id, ClinicId = receivable.ClinicId, ReceivableId = ReceivableId, Amount = 100 - remaining, Status = PaymentStatus.Paid });
            if (remaining > 0) db.Payments.Add(new Payment { ClinicId = receivable.ClinicId, ReceivableId = ReceivableId, Amount = remaining, Status = PaymentStatus.Paid });
            await db.SaveChangesAsync();
        });
        return id;
    }
}
