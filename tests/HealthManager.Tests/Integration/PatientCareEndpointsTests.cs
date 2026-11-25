using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HealthManager.Application;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests.Integration;

public class PatientCareEndpointsTests
{
    [Fact]
    public async Task Native_patient_workflows_supply_twenty_reports_without_imports()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var clinic = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var patient = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var doctor = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var appointment = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var receivable = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var payment = Guid.NewGuid();
        var application = Guid.NewGuid();
        var day = new DateOnly(2026, 10, 1);
        await factory.WithDbContextAsync(async db => {
            var slot = await db.Appointments.SingleAsync(x => x.Id == appointment);
            slot.StartAt = new DateTimeOffset(2026, 10, 1, 11, 0, 0, TimeSpan.Zero);
            var bill = await db.Receivables.SingleAsync(x => x.Id == receivable);
            bill.OriginalAmount = 150; bill.DueDate = slot.StartAt.AddMonths(2);
            db.Payments.Add(new() { Id = payment, ClinicId = clinic, ReceivableId = receivable, PaidAt = slot.StartAt, Amount = 40 });
            db.Payments.Add(new() { ClinicId = clinic, ReceivableId = receivable, PaidAt = slot.StartAt.AddMonths(1), Amount = 10 });
            var product = new Product { Name = "Insumo de teste", ClinicId = clinic };
            db.Products.Add(product);
            db.PatientApplications.Add(new() { Id = application, ClinicId = clinic, PatientId = patient, ProductId = product.Id, AppliedAt = slot.StartAt });
            await db.SaveChangesAsync();
        });
        string Route(string source) => $"/patients/{patient}/care/{source}";
        async Task<CareItem> Save(string source, Dictionary<string,string> fields, Dictionary<string,Guid>? links = null, CareItem? old = null)
        {
            var res = await client.PutAsJsonAsync(Route(source) + "/" + (old?.Id ?? Guid.NewGuid()),
                new CareInput(old?.Revision ?? 0, day, fields, links ?? []));
            res.EnsureSuccessStatusCode();
            return (await res.Content.ReadFromJsonAsync<CareItem>())!;
        }
        async Task<decimal?[]> Metrics(string report, string end = "2026-10-31")
        {
            var result = await client.GetFromJsonAsync<ReportResult>($"/reports/{report}?from=2026-10-01&to={end}");
            return result!.Rows.SelectMany(x => x.Metrics.Select(m => m.Value)).ToArray();
        }
        await Save("reservas", [], new() { ["pagamento"] = payment });
        await Save("espera", []);
        var arrival = await Save("horarios", new() { ["chegada"] = "08:02" }, new() { ["consulta"] = appointment });
        (await Metrics("pontualidade")).Should().BeEmpty();
        await Save("horarios", new() { ["chegada"] = "08:02", ["inicio"] = "08:12" }, arrival.Links, arrival);
        var indication = await Save("indicacoes", new() { ["tratamento"] = "Protocolo A" }, new() { ["consulta"] = appointment });
        (await Metrics("conversao-prescricoes")).Last().Should().Be(0m);
        var contract = await Save("programas", new() { ["modalidade"] = "Mensal", ["vencimento"] = "2026-10-31" }, new() { ["medico"] = doctor });
        Dictionary<string, Guid> program = new() { ["programa"] = contract.Id };
        await Save("indicacoes", indication.Fields, new(program) { ["consulta"] = appointment }, indication);
        foreach (var category in new[] { "Consulta", "Avaliacao", "Outro" })
        {
            Dictionary<string, string> fields = new() { ["categoria"] = category, ["servico"] = category, ["quantidade"] = "1", ["previsto"] = "2026-11-10" };
            if (category == "Outro") { fields["entrega"] = "2026-10-10"; fields["receita"] = "200"; fields["custo"] = "30"; }
            await Save("servicos", fields, category == "Consulta" ? new(program) { ["consulta"] = appointment } : program);
        }
        await Save("parcelas", [], new(program) { ["titulo"] = receivable });
        await Save("etapas", new() { ["protocolo"] = "A" }, new() { ["aplicacao"] = application });
        await Save("interrupcoes", new() { ["protocolo"] = "B" });
        await Save("insumos", new() { ["insumo"] = "Material", ["quantidade"] = "1,25", ["unidade"] = "mL" }, new() { ["aplicacao"] = application });
        (await Metrics("novos-agendamentos")).Should().Equal(1m, 40m);
        (await Metrics("demanda")).Should().Equal(1m);
        (await Metrics("pontualidade")).Should().Equal(12m, 10m);
        (await Metrics("conversao-consulta")).Last().Should().Be(100m);
        (await Metrics("conversao-prescricoes")).Last().Should().Be(100m);
        (await Metrics("carteira")).Should().Equal(1m);
        (await Metrics("programas-vendidos")).Should().Equal(1m);
        (await Metrics("vencimentos")).Should().Equal(0m);
        (await Metrics("renovacao")).Should().Equal(0m, 1m, 0m);
        (await Metrics("abandono-programa")).Should().Equal(0m, 1m, 0m);
        (await Metrics("servicos")).Should().Equal(1m, 0m, 1m, 0m, 1m, 1m);
        (await Metrics("consultas-pendentes")).Should().Equal(1m);
        (await Metrics("avaliacoes-pendentes")).Should().Equal(1m);
        (await Metrics("obrigacoes")).Should().Equal(1m, 1m);
        (await Metrics("custo-programa")).Should().Equal(30m);
        (await Metrics("margem-modalidade")).Should().Equal(200m, 30m, 170m, 85m);
        (await Metrics("receita-futura")).Should().Equal(110m);
        (await Metrics("receita-futura", "2026-11-30")).Should().Equal(100m);
        (await Metrics("adesao")).Should().Equal(1m, 1m, 100m);
        (await Metrics("abandono-tratamento")).Should().Equal(1m);
        (await Metrics("insumos")).Should().Equal(1.25m);
        await factory.WithDbContextAsync(async db => {
            (await db.Appointments.SingleAsync(x => x.Id == appointment)).Status = AppointmentStatus.Completed;
            await db.SaveChangesAsync();
        });
        (await Metrics("consultas-pendentes")).Should().BeEmpty();
        (await Metrics("servicos")).Should().Equal(1m, 0m, 1m, 1m, 1m, 1m);
        (await client.PutAsJsonAsync(Route("reservas") + "/" + Guid.NewGuid(), new CareInput(0, day, [], new() { ["pagamento"] = payment }))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PutAsJsonAsync(Route("horarios") + "/" + arrival.Id, new CareInput(arrival.Revision, day, arrival.Fields, arrival.Links))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PutAsJsonAsync(Route("servicos") + "/" + Guid.NewGuid(), new CareInput(0, day, [], new() { ["programa"] = Guid.NewGuid() }))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await factory.SeedSecondClinicPatientAsync();
        (await client.GetAsync("/patients/99999999-9999-9999-9999-999999999999/care/espera")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var physician = await factory.CreateAuthenticatedClientAsync("henrique.lima@clinicaaurora.com", "ChangeMe123!");
        (await physician.GetAsync(Route("parcelas"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var saved = await client.GetFromJsonAsync<CareWorkspace>(Route("indicacoes"));
        saved!.Records.Single().Fields["tratamento"].Should().Be("Protocolo A");
    }
}
