using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HealthManager.Application;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests.Integration;

public sealed class ReportsEndpointsTests
{
    private const string Period = "?from=2026-10-01&to=2026-10-31";

    [Fact]
    public async Task Catalog_reports_and_imports_use_real_records_and_reject_other_roles()
    {
        await using var factory = new ApiTestFactory();
        using var admin = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        using var doctor = await factory.CreateAuthenticatedClientAsync("henrique.lima@clinicaaurora.com", "ChangeMe123!");
        (await doctor.GetAsync("/reports")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await doctor.GetAsync("/reports/ltv" + Period)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await doctor.PostAsJsonAsync("/reports/sources/vendas/import", new { csv = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.GetAsync("/reports/carteira?from=2024-01-01&to=2026-01-01")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var directory = (await admin.GetFromJsonAsync<ReportDirectory>("/reports"))!;
        directory.Reports.Select(r => r.Id).Distinct().Should().HaveCount(42);
        directory.Reports.GroupBy(x => x.Group).Select(g => g.Count()).Should().BeEquivalentTo([15, 13, 14]);

        var records = new Dictionary<string, string> {
            ["reservas"] = "p1;120,50",
            ["espera"] = "p1;",
            ["horarios"] = "08:00;08:05;08:15",
            ["indicacoes"] = "p1;c1;2026-10-15",
            ["programas"] = "p1;Dra. A;Mensal;2026-10-31;;;2026-10-10",
            ["servicos"] = "P1;Mensal;Avaliação;Avaliacao;3;2026-10-16;2026-10-16;300;75",
            ["parcelas"] = "P1;2026-11-01;;150",
            ["vendas"] = "p1;Injetavel;Produto A;2;100;;;",
            ["etapas"] = "p1;Protocolo A;2026-10-01",
            ["interrupcoes"] = "p1;Protocolo A",
            ["insumos"] = "A1;Material A;mL;1,25"
        };
        foreach (var source in directory.Sources)
        {
            var csv = string.Join(';', source.Header) + "\nref;2026-10-01;" + records[source.Id];
            (await admin.PostAsJsonAsync($"/reports/sources/{source.Id}/import", new ReportCsv(csv))).EnsureSuccessStatusCode();
        }
        foreach (var report in directory.Reports)
            (await admin.GetAsync($"/reports/{report.Id}{Period}")).EnsureSuccessStatusCode();

        async Task<ReportRow> Row(string id) { var rows = (await admin.GetFromJsonAsync<ReportResult>($"/reports/{id}{Period}"))!.Rows; return id == "novos-agendamentos" ? rows.First() : rows.Single(); }
        (await Row("pontualidade")).Metrics.Select(m => m.Value).Should().Equal(15m, 10m);
        (await Row("novos-agendamentos")).Metrics.Select(m => m.Value).Should().Equal(1m, 120.50m);
        var details = (await admin.GetFromJsonAsync<ReportResult>("/reports/novos-agendamentos" + Period))!.Rows;
        details.Should().HaveCount(2);
        details[1].Label.Should().Contain("p1").And.Contain("01/10/2026");
        (await Row("margem-modalidade")).Metrics.Select(m => m.Value).Should().Equal(300m, 75m, 225m, 75m);
        (await Row("margem-injetavel")).Metrics.Last().Value.Should().BeNull();
        (await Row("receita-futura")).Metrics.Single().Value.Should().Be(150m);
        (await Row("carteira")).Metrics.Single().Value.Should().Be(1m);
        (await Row("insumos")).Metrics.Single().Value.Should().Be(1.25m);

        var header = string.Join(';', directory.Sources.Single(s => s.Id == "reservas").Header);
        var correction = new ReportCsv(header + "\nref;2026-10-02;p1;70");
        (await admin.PostAsJsonAsync("/reports/sources/reservas/import", correction)).EnsureSuccessStatusCode();
        var invalid = new ReportCsv(correction.Csv + "\nref2;2026-10-02;p2;-5");
        (await admin.PostAsJsonAsync("/reports/sources/reservas/import", invalid)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await factory.WithDbContextAsync(async db => {
            db.ReportSourceRecords.Add(new() { Source = "reservas", Reference = "ref", ClinicId = Guid.NewGuid(),
                Date = new(2026, 10, 2), ValuesJson = """{"paciente":"outro","valor":"1000"}""" });
            await db.SaveChangesAsync();
        });
        (await Row("novos-agendamentos")).Metrics.Select(m => m.Value).Should().Equal(1m, 70m);
        var page = await admin.GetFromJsonAsync<SourcePage>("/reports/sources/reservas" + Period);
        page!.Csv.Should().Contain("70").And.NotContain("1000");
    }

    [Fact]
    public async Task New_appointments_show_the_linked_patient_name()
    {
        await using var factory = new ApiTestFactory();
        using var admin = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var patientId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        string name = "";
        await factory.WithDbContextAsync(async db => name = (await db.Patients.SingleAsync(x => x.Id == patientId)).Name);
        var csv = $"referencia;data;paciente;valor\nsinal;2026-10-01;{patientId};60";
        (await admin.PostAsJsonAsync("/reports/sources/reservas/import", new ReportCsv(csv))).EnsureSuccessStatusCode();
        var report = (await admin.GetFromJsonAsync<ReportResult>("/reports/novos-agendamentos" + Period))!;
        report.Rows.Should().HaveCount(2);
        report.Rows[1].Label.Should().Contain(name).And.Contain("01/10/2026");
        report.Rows[1].Metrics.Last().Value.Should().Be(60);
    }

    [Fact]
    public async Task Occupancy_deduplicates_overlaps_and_sales_keep_the_price_at_grant()
    {
        await using var factory = new ApiTestFactory();
        using var admin = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        await factory.WithDbContextAsync(async db => {
            var source = await db.Appointments.IgnoreQueryFilters().FirstAsync();
            var start = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
            for (var i = 0; i < 2; i++)
                db.Appointments.Add(new() { ClinicId = source.ClinicId, PatientId = source.PatientId, DoctorId = source.DoctorId,
                    AppointmentTypeId = source.AppointmentTypeId, StartAt = start, EndAt = start.AddHours(1), Status = AppointmentStatus.Completed });
            db.DoctorAvailabilities.Add(new() { ClinicId = source.ClinicId, DoctorId = source.DoctorId, DayOfWeek = DayOfWeek.Thursday,
                StartTime = TimeSpan.FromHours(9), EndTime = TimeSpan.FromHours(11) });
            db.DoctorAvailabilities.Add(new() { ClinicId = source.ClinicId, DoctorId = source.DoctorId, DayOfWeek = DayOfWeek.Friday,
                StartTime = TimeSpan.FromHours(23), EndTime = TimeSpan.FromDays(1) });
            await db.SaveChangesAsync();
        });
        var midnight = await admin.GetFromJsonAsync<ReportResult>("/reports/ocupacao?from=2026-10-02&to=2026-10-02");
        midnight!.Rows.Single().Metrics.Select(x => x.Value).Should().Equal(1m, 0m, 0m);
        var result = await admin.GetFromJsonAsync<ReportResult>("/reports/ocupacao?from=2026-10-01&to=2026-10-01");
        result!.Rows.Single().Metrics.Select(x => x.Value).Should().Equal(2m, 1m, 50m);
        var profile = await admin.GetFromJsonAsync<ReportResult>("/reports/perfil?from=2026-10-01&to=2026-10-01");
        profile!.Rows.Should().HaveCount(2);
        profile.Rows.Single(x => x.Label == "Primeiro atendimento").Metrics.Single().Value.Should().Be(1);
        var product = await admin.PostAsJsonAsync("/products", new { name = "Produto teste", price = 85m, applicationCount = 3 });
        var id = (await product.Content.ReadFromJsonAsync<ProductResponse>())!.Id;
        (await admin.PostAsJsonAsync("/patients/dddddddd-dddd-dddd-dddd-dddddddddddd/applications/balance",
            new { source = "individual", productId = id, treatmentClass = "Injetavel" })).EnsureSuccessStatusCode();
        await factory.WithDbContextAsync(async db => {
            (await db.Products.IgnoreQueryFilters().SingleAsync(x => x.Id == id)).Price = 999m;
            await db.SaveChangesAsync();
        });
        var today = (await admin.GetFromJsonAsync<ReportDirectory>("/reports"))!.Today.ToString("yyyy-MM-dd");
        var sales = await admin.GetFromJsonAsync<ReportResult>($"/reports/vendas-injetavel?from={today}&to={today}");
        sales!.Rows.Single().Metrics.Select(x => x.Value).Should().Equal(1m, 85m);
    }
}
