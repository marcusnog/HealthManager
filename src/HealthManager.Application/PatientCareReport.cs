using System.Globalization;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Application;

internal static class PatientCareReport
{
    internal static async Task<List<SourceLine>> ReadAsync(IApplicationDbContext db, Guid clinic, string source,
        DateOnly from, DateOnly to, TimeZoneInfo zone, CancellationToken ct)
    {
        var end = ReportDates.Utc(to.AddDays(1), TimeOnly.MinValue, zone);
        var query = db.PatientCareRecords.AsNoTracking().Where(x => x.ClinicId == clinic && x.Source == source);
        if (source == "reservas")
        {
            var start = ReportDates.Utc(from, TimeOnly.MinValue, zone);
            var deposits = await query.Where(x => x.Payment!.Status == PaymentStatus.Paid && x.Payment.PaidAt >= start && x.Payment.PaidAt < end)
                .Select(x => new { x.Id, x.PatientId, x.Payment!.PaidAt, x.Payment.Amount }).ToListAsync(ct);
            return deposits.Select(x => new SourceLine(PatientCareService.Local(x.PaidAt, zone),
                new() { ["paciente"] = x.PatientId.ToString(), ["valor"] = x.Amount.ToString(CultureInfo.InvariantCulture) }, x.Id.ToString())).ToList();
        }
        query = query.Where(x => x.Date <= to);
        if (source is "horarios" or "indicacoes" or "etapas" or "interrupcoes" or "insumos") query = query.Where(x => x.Date >= from);
        if (source == "parcelas")
        {
            var titles = await query.Where(x => x.Contract != null && x.Contract.Date <= to && x.Receivable != null &&
                    x.Receivable.Status != ReceivableStatus.Cancelled && x.Receivable.DueDate >= end)
                .Select(x => new { x.Id, x.Date, Contract = x.Contract!, x.Receivable!.DueDate, x.Receivable.OriginalAmount,
                    Paid = x.Receivable.Payments.Where(p => p.Status == PaymentStatus.Paid && p.PaidAt < end).Sum(p => p.Amount) }).ToListAsync(ct);
            return titles.Where(x => x.OriginalAmount > x.Paid).Select(x => new SourceLine(x.Date, new() {
                ["programa"] = PatientCareService.ContractLabel(x.Contract),
                ["vencimento"] = Iso(PatientCareService.Local(x.DueDate, zone)),
                ["valor"] = (x.OriginalAmount - x.Paid).ToString(CultureInfo.InvariantCulture)
            }, x.Id.ToString())).ToList();
        }
        if (source == "programas") query = query.Include(x => x.Doctor);
        if (source is "servicos" or "indicacoes") query = query.Include(x => x.Contract);
        if (source == "servicos") query = query.Include(x => x.Appointment);
        if (source is "etapas" or "insumos") query = query.Include(x => x.Application);
        var rows = await query.ToListAsync(ct);
        List<SourceLine> lines = [];
        foreach (var row in rows)
        {
            var fields = PatientCareService.Details(row);
            fields["paciente"] = row.PatientId.ToString();
            if (row.AppointmentId.HasValue) fields["consulta"] = row.AppointmentId.Value.ToString();
            if (source == "horarios" && !fields.ContainsKey("inicio")) continue;
            if (source == "programas") fields["medico"] = row.Doctor is { } doctor ? $"{doctor.Name} · {doctor.Crm}" : "Profissional indisponível";
            if (source == "indicacoes" && row.Contract != null) fields["contratacao"] = Iso(row.Contract.Date);
            if (source == "servicos")
            {
                if (row.Contract is null || row.Contract.Date > to) continue;
                fields["programa"] = PatientCareService.ContractLabel(row.Contract);
                fields["modalidade"] = PatientCareService.Details(row.Contract)["modalidade"];
                if (row.Appointment is { Status: AppointmentStatus.Completed } visit)
                    fields["entrega"] = Iso(PatientCareService.Local(visit.StartAt, zone));
            }
            if (row.Application is { } application)
            {
                fields["aplicacao"] = application.Id.ToString();
                fields["execucao"] = Iso(PatientCareService.Local(application.AppliedAt, zone));
            }
            else if (source == "insumos") continue;
            lines.Add(new(row.Date, fields, row.Id.ToString()));
        }
        return lines;
    }

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
