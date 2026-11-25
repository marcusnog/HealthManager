using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HealthManager.Application;
using HealthManager.Domain;

namespace HealthManager.Tests.Integration;

public class EvidenceSearchTests
{
    [Fact]
    public async Task Search_is_read_only_scoped_and_distinguishes_file_names_from_readable_contents()
    {
        await using var factory = new ApiTestFactory();
        var clinic = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var patient = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        await factory.SeedSecondClinicPatientAsync();
        await factory.WithDbContextAsync(async db => {
            (await db.Patients.FindAsync(patient))!.Notes = "Contrato PLUS vigente";
            db.PatientDocuments.Add(new PatientDocument { ClinicId = clinic, PatientId = patient, FileName = "next.png", ContentType = "image/png" });
            await db.SaveChangesAsync();
        });
        using var admin = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        async Task<EvidencePage> Search(string source) {
            var response = await admin.PostAsJsonAsync("/reports/evidence/search", new EvidenceRequest("plus,next", source));
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<EvidencePage>())!;
        }
        var records = await Search("fichas");
        records.Hits.Should().ContainSingle(x => x.PatientId == patient && x.Excerpt.Contains("PLUS"));
        records.Hits.Should().OnlyContain(x => x.PatientId == patient);
        var files = await Search("documentos");
        files.Manual.Should().BeGreaterThan(0);
        files.Hits.Should().ContainSingle(x => x.Origin == "next.png" && x.PatientId == patient);
        (await admin.PostAsJsonAsync("/reports/evidence/search", new EvidenceRequest("", "fichas"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doctor = await factory.CreateAuthenticatedClientAsync("henrique.lima@clinicaaurora.com", "ChangeMe123!");
        (await doctor.PostAsJsonAsync("/reports/evidence/search", new EvidenceRequest("PLUS", "fichas"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
