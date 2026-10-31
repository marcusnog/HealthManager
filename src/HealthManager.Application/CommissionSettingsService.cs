using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Application;

public sealed record CommissionRule(bool Enabled, [Required] string Mode, decimal Value, [Required] string Basis);
public sealed record CommissionSettings(
    [Required] CommissionRule AppointmentSale,
    [Required] CommissionRule CompletedAppointment,
    [Required] CommissionRule ProductReferral);

public sealed class CommissionSettingsService(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
{
    public async Task<CommissionSettings> GetAsync(CancellationToken ct)
    {
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var clinic = await dbContext.Clinics.AsNoTracking().SingleOrDefaultAsync(x => x.Id == clinicId, ct)
            ?? throw new KeyNotFoundException("Clinica nao encontrada.");
        return Read(clinic);
    }

    public async Task<CommissionSettings> UpdateAsync(CommissionSettings settings, CancellationToken ct)
    {
        Validate(settings.AppointmentSale, ["Deposit", "Consultation"]);
        Validate(settings.CompletedAppointment, ["Consultation"]);
        Validate(settings.ProductReferral, ["ProductSale", "ProductReceived"]);
        var clinicId = TenantGuard.RequireClinicId(tenantProvider);
        var clinic = await dbContext.Clinics.SingleOrDefaultAsync(x => x.Id == clinicId, ct)
            ?? throw new KeyNotFoundException("Clinica nao encontrada.");
        var previous = Read(clinic);
        clinic.CommissionSettingsJson = JsonSerializer.Serialize(settings);
        clinic.UpdatedAt = DateTimeOffset.UtcNow;
        dbContext.AuditLogs.Add(new AuditLog
        {
            ClinicId = clinicId, UserId = tenantProvider.UserId,
            Action = "commission-settings.updated", EntityName = nameof(Clinic), EntityId = clinicId,
            PayloadJson = JsonSerializer.Serialize(new { before = previous, after = settings })
        });
        await dbContext.SaveChangesAsync(ct);
        return settings;
    }

    private static CommissionSettings Read(Clinic clinic) => clinic.CommissionSettingsJson is null
        ? new(new(false, "Fixed", 0, "Deposit"), new(false, "Fixed", 0, "Consultation"), new(false, "Percentage", 10, "ProductReceived"))
        : JsonSerializer.Deserialize<CommissionSettings>(clinic.CommissionSettingsJson)!;

    private static void Validate(CommissionRule? rule, string[] bases)
    {
        if (rule is null || rule.Mode is not ("Fixed" or "Percentage") || !bases.Contains(rule.Basis))
            throw new InvalidOperationException("Tipo ou base de comissao invalida.");
        if (rule.Value < 0 || rule.Value > 1000000 || decimal.Round(rule.Value, 2) != rule.Value ||
            (rule.Enabled && rule.Value == 0) || (rule.Mode == "Percentage" && rule.Value > 100))
            throw new InvalidOperationException("Informe um valor positivo com ate duas casas decimais. Percentual limitado a 100% e valor fixo a R$ 1.000.000,00.");
    }
}
