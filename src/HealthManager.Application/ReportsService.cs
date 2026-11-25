using System.Text.Json;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;
using static HealthManager.Application.SourceReportCalculations;

namespace HealthManager.Application;

public sealed class ReportsService(IApplicationDbContext db, ITenantProvider tenant)
{
    private async Task<TimeZoneInfo> Zone(Guid clinic, CancellationToken ct) =>
        AppHelpers.ResolveTimeZone(await db.Clinics.Where(x => x.Id == clinic).Select(x => x.Timezone).FirstAsync(ct));

    public async Task<ReportDirectory> DirectoryAsync(CancellationToken ct)
    {
        var zone = await Zone(TenantGuard.RequireClinicId(tenant), ct);
        var catalog = ReportCatalog.Instance;
        return new(catalog.Reports, catalog.Sources, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime));
    }

    public async Task<ReportResult> RunAsync(string id, DateOnly from, DateOnly to, CancellationToken ct)
    {
        ReportDates.Validate(from, to);
        var clinic = TenantGuard.RequireClinicId(tenant);
        var report = ReportCatalog.Instance.Reports.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("Relatório desconhecido.");
        if (report.Source != null)
        {
            var query = db.ReportSourceRecords.AsNoTracking().Where(x => x.ClinicId == clinic && x.Source == report.Source && x.Date <= to);
            if (id is not ("demanda" or "carteira" or "vencimentos" or "renovacao" or "abandono-programa" or "consultas-pendentes"
                or "avaliacoes-pendentes" or "receita-futura" or "obrigacoes" or "custo-programa" or "margem-modalidade" or "recompra"))
                query = query.Where(x => x.Date >= from);
            var records = await query.Select(x => new { x.Date, x.ValuesJson, x.Reference }).ToListAsync(ct);
            var lines = records.Select(x => new SourceLine(x.Date, JsonSerializer.Deserialize<Dictionary<string, string>>(x.ValuesJson)!, x.Reference)).ToList();
            return new(lines.Count == 0 ? [] : Calculate(id, lines, from, to));
        }
        var zone = await Zone(clinic, ct);
        var start = ReportDates.Utc(from, TimeOnly.MinValue, zone);
        var end = ReportDates.Utc(to.AddDays(1), TimeOnly.MinValue, zone);
        if (id == "ltv")
        {
            var totals = await db.Payments.Where(x => x.ClinicId == clinic && x.Status == PaymentStatus.Paid && x.PaidAt < end)
                .Select(x => new { PatientId = x.Receivable!.AppointmentId != null ? (Guid?)x.Receivable.Appointment!.PatientId : x.Receivable.PatientId, x.ClinicRevenueAmount })
                .Where(x => x.PatientId != null).GroupBy(x => x.PatientId)
                .Select(g => new { Id = g.Key, Revenue = g.Sum(x => x.ClinicRevenueAmount) })
                .Join(db.Patients.Where(x => x.ClinicId == clinic), x => x.Id, p => p.Id, (x, p) => new { p.Name, p.MedicalRecordNumber, x.Revenue })
                .OrderByDescending(x => x.Revenue).ToListAsync(ct);
            return new(totals.Select(x => new ReportRow($"{x.Name} · {x.MedicalRecordNumber}", [new("Receita acumulada", x.Revenue, "brl")])).ToList());
        }
        if (id == "aplicacoes-realizadas")
        {
            var groups = await db.PatientApplications.Where(x => x.ClinicId == clinic && x.AppliedAt >= start && x.AppliedAt < end)
                .GroupBy(x => x.Product!.Name).Select(g => new { Name = g.Key, Units = g.Sum(x => x.Quantity) }).OrderBy(x => x.Name).ToListAsync(ct);
            return new(groups.Select(g => new ReportRow(g.Name, [new("Aplicações realizadas", g.Units)])).ToList());
        }
        if (id == "aplicacoes-pendentes")
        {
            var groups = await db.PatientProductBalances.Where(x => x.ClinicId == clinic && x.PurchasedUnits > x.UsedUnits)
                .GroupBy(x => x.Product!.Name).Select(g => new { Name = g.Key, Units = g.Sum(x => x.PurchasedUnits - x.UsedUnits) }).OrderBy(x => x.Name).ToListAsync(ct);
            return new(groups.Select(g => new ReportRow(g.Name, [new("Aplicações pendentes", g.Units)])).ToList());
        }

        var appointments = await db.Appointments.AsNoTracking().Where(x => x.ClinicId == clinic && x.StartAt < end && x.EndAt > start)
            .Select(x => new Slot(x.Id, x.DoctorId, x.Doctor!.Name, x.CreatedAt, x.StartAt, x.EndAt, x.Status,
                x.ConfirmationStatus, x.TracksRescheduling, x.RescheduledAt, x.CancelledAt)).ToListAsync(ct);
        var period = appointments.Where(x => x.Start >= start).ToList();
        var completed = period.Where(x => x.Status == AppointmentStatus.Completed).ToList();
        var active = period.Where(x => x.Status != AppointmentStatus.Cancelled).ToList();
        switch (id)
        {
            case "no-show":
                var missed = period.Count(x => x.Status == AppointmentStatus.NoShow);
                return new(Rate("Faltas", missed, missed + completed.Count));
            case "confirmacao":
                return new(Rate("Consultas confirmadas", active.Count(x => x.Confirmation == ConfirmationStatus.Confirmed || x.Status == AppointmentStatus.Confirmed), active.Count));
            case "cancelamento":
                return new(Rate("Consultas canceladas", period.Count - active.Count, period.Count));
            case "remarcacao":
                var tracked = period.Where(x => x.Tracked).ToList();
                return new(Rate("Consultas remarcadas", tracked.Count(x => x.Rescheduled != null), tracked.Count));
            case "recuperacao":
                var slots = period.Where(x => x.Status == AppointmentStatus.Cancelled && x.Cancelled <= x.Start)
                    .GroupBy(x => (x.Doctor, x.Start, x.End))
                    .Select(g => g.MaxBy(x => x.Cancelled)!)
                    .Where(slot => !appointments.Any(a => a.Doctor == slot.Doctor && a.Start < slot.End && a.End > slot.Start
                        && a.Created <= slot.Cancelled && (a.Status != AppointmentStatus.Cancelled || a.Cancelled > slot.Cancelled)))
                    .ToList();
                var recovered = slots.Count(slot => appointments.Any(a => a.Status != AppointmentStatus.Cancelled && a.Doctor == slot.Doctor
                    && a.Created > slot.Cancelled && a.Created <= slot.Start && a.Start <= slot.Start && a.End >= slot.End));
                return new(Rate("Horários recuperados", recovered, slots.Count));
            case "antecedencia":
                var days = period.Where(x => x.Created <= x.Start).Select(x => (decimal)(x.Start - x.Created).TotalDays).ToList();
                return new(days.Count == 0 ? [] : [new("Total", [new("Antecedência média", days.Average(), "dias")])]);
            case "produtividade":
                return new(completed.GroupBy(x => new { x.Doctor, x.Name }).OrderBy(g => g.Key.Name)
                    .Select(g => new ReportRow(g.Key.Name, [new("Atendimentos", g.Count())])).ToList());
            case "perfil":
                var encounters = db.Appointments.Where(x => x.ClinicId == clinic && x.Status == AppointmentStatus.Completed);
                var profiles = await encounters.Where(x => x.StartAt >= start && x.StartAt < end)
                    .Select(x => new { Type = x.AppointmentType.Name, First = encounters.Where(a => a.PatientId == x.PatientId)
                        .OrderBy(a => a.StartAt).ThenBy(a => a.Id).Select(a => a.Id).First() == x.Id }).ToListAsync(ct);
                return new(profiles.GroupBy(x => x.First ? "Primeiro atendimento" : $"Seguimento · {x.Type}").OrderBy(g => g.Key)
                    .Select(g => new ReportRow(g.Key, [new("Atendimentos", g.Count())])).ToList());
            default: return await CapacityAsync(id, clinic, zone, from, to, end, appointments, completed, ct);
        }
    }

    private sealed record Slot(Guid Id, Guid Doctor, string Name, DateTimeOffset Created, DateTimeOffset Start,
        DateTimeOffset End, AppointmentStatus Status, ConfirmationStatus Confirmation, bool Tracked, DateTimeOffset? Rescheduled, DateTimeOffset? Cancelled);
    private readonly record struct Interval(DateTimeOffset Start, DateTimeOffset End)
    {
        public decimal Hours => (decimal)(End - Start).TotalHours;
    }

    private static List<Interval> Merge(IEnumerable<Interval> ranges)
    {
        var result = new List<Interval>();
        foreach (var range in ranges.Where(x => x.Start < x.End).OrderBy(x => x.Start))
        {
            if (result.Count == 0 || result[^1].End < range.Start) result.Add(range);
            else if (range.End > result[^1].End) result[^1] = new(result[^1].Start, range.End);
        }
        return result;
    }

    private async Task<ReportResult> CapacityAsync(string id, Guid clinic, TimeZoneInfo zone, DateOnly from, DateOnly to, DateTimeOffset cutoff,
        List<Slot> appointments, List<Slot> completed, CancellationToken ct)
    {
        var revenues = new Dictionary<Guid, decimal>();
        if (id != "ocupacao")
        {
            var start = ReportDates.Utc(from, TimeOnly.MinValue, zone);
            revenues = await db.Payments.Where(x => x.ClinicId == clinic && x.Status == PaymentStatus.Paid && x.PaidAt < cutoff
                    && x.Receivable!.Appointment!.Status == AppointmentStatus.Completed && x.Receivable.Appointment.StartAt >= start && x.Receivable.Appointment.StartAt < cutoff)
                .GroupBy(x => x.Receivable!.Appointment!.DoctorId).Select(g => new { Id = g.Key, Revenue = g.Sum(x => x.ClinicRevenueAmount) })
                .ToDictionaryAsync(x => x.Id, x => x.Revenue, ct);
        }
        var shifts = await db.DoctorAvailabilities.AsNoTracking().Where(x => x.ClinicId == clinic && x.IsAvailable)
            .Select(x => new { x.DoctorId, x.Doctor.Name, x.DayOfWeek, x.StartTime, x.EndTime }).ToListAsync(ct);
        var doctors = shifts.Select(x => (Id: x.DoctorId, x.Name)).Concat(appointments.Select(x => (Id: x.Doctor, x.Name))).Distinct().OrderBy(x => x.Name);
        var rows = new List<ReportRow>();
        foreach (var doctor in doctors)
        {
            var worked = Merge(completed.Where(x => x.Doctor == doctor.Id).Select(x => new Interval(x.Start, x.End))).Sum(x => x.Hours);
            var revenue = revenues.GetValueOrDefault(doctor.Id);
            var hourly = Divide(revenue, worked);
            if (id == "receita-hora")
            {
                if (worked > 0) rows.Add(new(doctor.Name, [new("Receita reconhecida", revenue, "brl"), new("Horas de atendimento", worked, "h"), new("Receita por hora", hourly, "brl")]));
                continue;
            }
            var weekly = shifts.Where(x => x.DoctorId == doctor.Id).ToLookup(x => x.DayOfWeek);
            var ranges = new List<Interval>();
            for (var day = from; day <= to; day = day.AddDays(1))
                foreach (var shift in weekly[day.DayOfWeek])
                {
                    if (shift.StartTime < TimeSpan.Zero || shift.EndTime > TimeSpan.FromDays(1) || shift.EndTime <= shift.StartTime) continue;
                    var shiftEnd = shift.EndTime == TimeSpan.FromDays(1)
                        ? ReportDates.Utc(day.AddDays(1), TimeOnly.MinValue, zone)
                        : ReportDates.Utc(day, TimeOnly.FromTimeSpan(shift.EndTime), zone);
                    ranges.Add(new(ReportDates.Utc(day, TimeOnly.FromTimeSpan(shift.StartTime), zone), shiftEnd));
                }
            var offered = Merge(ranges);
            var booked = Merge(appointments.Where(x => x.Doctor == doctor.Id && x.Status != AppointmentStatus.Cancelled).Select(x => new Interval(x.Start, x.End)));
            decimal used = 0;
            var index = 0;
            foreach (var shift in offered)
            {
                while (index < booked.Count && booked[index].End <= shift.Start) index++;
                for (var j = index; j < booked.Count && booked[j].Start < shift.End; j++)
                {
                    var overlap = new Interval(shift.Start > booked[j].Start ? shift.Start : booked[j].Start, shift.End < booked[j].End ? shift.End : booked[j].End);
                    used += overlap.Hours;
                }
            }
            var hours = offered.Sum(x => x.Hours);
            rows.Add(new(doctor.Name, id == "ocupacao"
                ? [new("Horas disponíveis", hours, "h"), new("Horas ocupadas", used, "h"), new("Ocupação", Divide(used * 100, hours), "percentual")]
                : [new("Horas vagas", hours - used, "h"), new("Receita potencial", hours == 0 ? null : (hours - used) * hourly, "brl")]));
        }
        return new(rows);
    }
}
