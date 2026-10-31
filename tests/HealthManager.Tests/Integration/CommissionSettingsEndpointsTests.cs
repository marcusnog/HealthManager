using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HealthManager.Application;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests.Integration;

public sealed class CommissionSettingsEndpointsTests
{
    private static CommissionSettings Settings => new(new(true, "Fixed", 15.50m, "Deposit"), new(true, "Percentage", 5, "Consultation"), new(true, "Percentage", 10, "ProductReceived"));

    [Fact]
    public async Task Admin_CanSaveAndReload_WithAudit_WithoutChangingOtherClinic()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        await factory.SeedSecondClinicPatientAsync();
        var defaults = await client.GetFromJsonAsync<CommissionSettings>("/financial/commission-settings");
        defaults!.AppointmentSale.Enabled.Should().BeFalse();
        defaults.ProductReferral.Should().Be(new CommissionRule(false, "Percentage", 10, "ProductReceived"));
        (await client.PutAsJsonAsync("/financial/commission-settings", Settings)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<CommissionSettings>("/financial/commission-settings")).Should().Be(Settings);
        await client.PutAsJsonAsync("/tenant/settings", new { name = "Aurora", timezone = "America/Sao_Paulo", businessHoursJson = "{\"start\":\"08:00\",\"end\":\"18:00\"}" });
        (await client.GetFromJsonAsync<CommissionSettings>("/financial/commission-settings")).Should().Be(Settings);
        await factory.WithDbContextAsync(async db =>
        {
            (await db.Clinics.IgnoreQueryFilters().SingleAsync(x => x.Id == Guid.Parse("22222222-2222-2222-2222-222222222222"))).CommissionSettingsJson.Should().BeNull();
            var audit = await db.AuditLogs.IgnoreQueryFilters().SingleAsync(x => x.Action == "commission-settings.updated");
            audit.UserId.Should().Be(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
            audit.PayloadJson.Should().Contain("before").And.Contain("after");
        });
    }

    [Theory]
    [InlineData(UserRole.Doctor)]
    [InlineData(UserRole.Secretary)]
    public async Task NonAdmin_CannotReadOrWrite(UserRole role)
    {
        await using var factory = new ApiTestFactory();
        await factory.WithDbContextAsync(async db =>
        {
            var user = await db.Users.IgnoreQueryFilters().SingleAsync(x => x.Email == "henrique.lima@clinicaaurora.com");
            user.Role = role;
            await db.SaveChangesAsync();
        });
        using var client = await factory.CreateAuthenticatedClientAsync("henrique.lima@clinicaaurora.com", "ChangeMe123!");
        (await client.GetAsync("/financial/commission-settings")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PutAsJsonAsync("/financial/commission-settings", Settings)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Percentage", 101, "Deposit", true)]
    [InlineData("Fixed", -1, "Deposit", true)]
    [InlineData("Fixed", 0, "Deposit", true)]
    [InlineData("Fixed", 1000001, "Deposit", false)]
    [InlineData("Fixed", 0.001, "Deposit", true)]
    [InlineData("Other", 10, "Deposit", true)]
    [InlineData("Fixed", 10, "ProductSale", true)]
    public async Task InvalidRules_AreRejectedAtomically(string mode, double value, string basis, bool enabled)
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var before = await client.GetFromJsonAsync<CommissionSettings>("/financial/commission-settings");
        var invalid = Settings with { AppointmentSale = new(enabled, mode, (decimal)value, basis) };
        (await client.PutAsJsonAsync("/financial/commission-settings", invalid)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetFromJsonAsync<CommissionSettings>("/financial/commission-settings")).Should().Be(before);
    }

    [Fact]
    public async Task MissingRules_AreRejected()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        (await client.PutAsJsonAsync("/financial/commission-settings", new { appointmentSale = Settings.AppointmentSale })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
