using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HealthManager.Application;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests.Integration;

public sealed class ClinicalWorkspaceEndpointsTests
{
    private const string Appointment = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";
    private const string Patient = "dddddddd-dddd-dddd-dddd-dddddddddddd";
    private static SaveClinicalCatalogRequest Model(bool personal = false) => new("Document", "Orientações", "Retorno", "Paciente: {{paciente}}", personal, [], null);
    private static Task<HttpClient> Doctor(ApiTestFactory f) => f.CreateAuthenticatedClientAsync("henrique.lima@clinicaaurora.com", "ChangeMe123!");
    private static Task<HttpClient> Admin(ApiTestFactory f) => f.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

    [Fact]
    public async Task PatientScreen_AllowsClinicalWorkspace_ButNotScheduleOrCatalogManagement()
    {
        await using var f = new ApiTestFactory();
        using var seed = await Doctor(f);
        await f.WithDbContextAsync(async db => {
            var user = await db.Users.SingleAsync(x => x.Email == "henrique.lima@clinicaaurora.com");
            user.ScreenAccessJson = "[\"pacientes\"]";
            await db.SaveChangesAsync();
        });
        using var doctor = await Doctor(f);
        (await doctor.PostAsJsonAsync($"/appointments/{Appointment}/clinical-record", new { chiefComplaint = "Retorno" })).EnsureSuccessStatusCode();
        (await doctor.GetAsync($"/appointments/{Appointment}/clinical-record")).EnsureSuccessStatusCode();
        (await doctor.GetAsync($"/appointments/{Appointment}/clinical-entries")).EnsureSuccessStatusCode();
        (await doctor.GetAsync("/clinical-catalog")).EnsureSuccessStatusCode();
        (await doctor.GetAsync("/appointments")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await doctor.PostAsJsonAsync("/clinical-catalog", Model())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PersonalModels_ArePrivate_SharedModelsCanBeManaged_AndStaleWritesConflict()
    {
        await using var f = new ApiTestFactory(); using var doctor = await Doctor(f); using var admin = await Admin(f);
        var personal = await (await doctor.PostAsJsonAsync("/clinical-catalog", Model(true))).Content.ReadFromJsonAsync<ClinicalCatalogResponse>();
        (await admin.GetFromJsonAsync<ClinicalCatalogResponse[]>("/clinical-catalog"))!.Should().NotContain(x => x.Id == personal!.Id);
        (await admin.PutAsJsonAsync($"/clinical-catalog/{personal!.Id}", Model() with { Revision = personal.Revision })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var shared = await (await admin.PostAsJsonAsync("/clinical-catalog", Model())).Content.ReadFromJsonAsync<ClinicalCatalogResponse>();
        (await doctor.PutAsJsonAsync($"/clinical-catalog/{shared!.Id}", Model() with { Revision = shared.Revision, Title = "Editado" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.DeleteAsync($"/clinical-catalog/{shared.Id}?revision={shared.Revision}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Documents_AreSnapshots_Idempotent_AndFinalizationBlocksNewEntries()
    {
        await using var f = new ApiTestFactory(); using var doctor = await Doctor(f);
        (await doctor.PostAsJsonAsync($"/appointments/{Appointment}/clinical-record", new { chiefComplaint = "Acompanhamento" })).EnsureSuccessStatusCode();
        var template = await (await doctor.PostAsJsonAsync("/clinical-catalog", Model())).Content.ReadFromJsonAsync<ClinicalCatalogResponse>();
        var request = new CreateClinicalEntryRequest(Guid.NewGuid(), "Document", template!.Id, "Orientações", "Paciente: {{paciente}}", null, "DigitalMock");
        var response = await doctor.PostAsJsonAsync($"/appointments/{Appointment}/clinical-entries", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var entry = (await response.Content.ReadFromJsonAsync<ClinicalEntryResponse>())!;
        entry.Content.Should().NotContain("{{paciente}}"); entry.SignatureMode.Should().Be("DigitalMock");
        var retried = await (await doctor.PostAsJsonAsync($"/appointments/{Appointment}/clinical-entries", request)).Content.ReadFromJsonAsync<ClinicalEntryResponse>();
        retried!.Id.Should().Be(entry.Id);
        (await doctor.PutAsJsonAsync($"/clinical-catalog/{template.Id}", Model() with { Content = "Alterado", Revision = template.Revision })).EnsureSuccessStatusCode();
        var entries = await doctor.GetFromJsonAsync<ClinicalEntryResponse[]>($"/appointments/{Appointment}/clinical-entries");
        entries!.Should().ContainSingle().Which.Content.Should().Be(entry.Content);
        (await doctor.PostAsync($"/appointments/{Appointment}/clinical-record/finalize", null)).EnsureSuccessStatusCode();
        (await doctor.PostAsJsonAsync($"/appointments/{Appointment}/clinical-entries", request with { ClientRequestId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await doctor.PostAsJsonAsync($"/appointments/{Appointment}/clinical-entries", request)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Entries_RejectCrossTenantPatients_AndAdminWrites()
    {
        await using var f = new ApiTestFactory(); using var doctor = await Doctor(f); using var admin = await Admin(f);
        await f.SeedSecondClinicPatientAsync();
        (await doctor.GetAsync("/patients/99999999-9999-9999-9999-999999999999/clinical-entries")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PostAsJsonAsync($"/appointments/{Appointment}/clinical-entries", new CreateClinicalEntryRequest(Guid.NewGuid(), "Document", null, "Teste", "Texto", null))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await f.WithDbContextAsync(async db => { db.ClinicalCatalogItems.Add(new ClinicalCatalogItem { ClinicId = Guid.Parse("22222222-2222-2222-2222-222222222222"), Title = "Segredo" }); await db.SaveChangesAsync(); });
        (await admin.GetFromJsonAsync<ClinicalCatalogResponse[]>("/clinical-catalog"))!.Should().NotContain(x => x.Title == "Segredo");
    }

    [Fact]
    public async Task Forms_ValidateRequiredAndNumericFields_AndKeepCorrectionHistory()
    {
        await using var f = new ApiTestFactory(); using var doctor = await Doctor(f);
        (await doctor.PostAsJsonAsync($"/appointments/{Appointment}/clinical-record", new { chiefComplaint = "Acompanhamento" })).EnsureSuccessStatusCode();
        var model = Model() with { Kind = "Questionnaire", Fields = [new("Resposta", "Number", true, null, 0, 10)] };
        var template = (await (await doctor.PostAsJsonAsync("/clinical-catalog", model)).Content.ReadFromJsonAsync<ClinicalCatalogResponse>())!;
        var request = new CreateClinicalEntryRequest(Guid.NewGuid(), "Questionnaire", template.Id, "Formulário", "", new() { ["Resposta"] = "11" });
        (await doctor.PostAsJsonAsync($"/appointments/{Appointment}/clinical-entries", request)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        request = request with { Values = new() { ["Resposta"] = "4" } };
        var entry = (await (await doctor.PostAsJsonAsync($"/appointments/{Appointment}/clinical-entries", request)).Content.ReadFromJsonAsync<ClinicalEntryResponse>())!;
        (await doctor.PostAsJsonAsync($"/appointments/{Appointment}/clinical-entries", request with { ClientRequestId = Guid.NewGuid(), CorrectsEntryId = entry.Id, Values = new() { ["Resposta"] = "5" } })).StatusCode.Should().Be(HttpStatusCode.Created);
        var history = await doctor.GetFromJsonAsync<PagedResult<ClinicalEntryResponse>>($"/patients/{Patient}/clinical-entries");
        history!.Total.Should().Be(2);
        history.Items.Select(x => x.Values["Resposta"]).Should().BeEquivalentTo(["4", "5"]);
    }

    [Fact]
    public async Task Catalog_RejectsInvalidDefaults_AndExcludedModulesCannotBeRecorded()
    {
        await using var f = new ApiTestFactory(); using var doctor = await Doctor(f);
        var invalid = Model() with { Kind = "Food", Fields = [new("Quantidade", "Number", false, "g", 0, 100, "-1")] };
        (await doctor.PostAsJsonAsync("/clinical-catalog", invalid)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var valid = invalid with { Fields = [new("Quantidade", "Number", false, "g", 0, 100, "25", 2, 10, 80)] };
        var saved = await doctor.PostAsJsonAsync("/clinical-catalog", valid);
        saved.StatusCode.Should().Be(HttpStatusCode.Created);
        (await saved.Content.ReadFromJsonAsync<ClinicalCatalogResponse>())!.Fields[0].DefaultValue.Should().Be("25");
        (await doctor.PostAsJsonAsync($"/appointments/{Appointment}/clinical-record", new { chiefComplaint = "Acompanhamento" })).EnsureSuccessStatusCode();
        foreach (var kind in new[] { "Measurement", "Telemedicine", "AI" })
            (await doctor.PostAsJsonAsync($"/appointments/{Appointment}/clinical-entries", new CreateClinicalEntryRequest(Guid.NewGuid(), kind, null, "Excluído", "Teste", null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Doctor_CannotOpenAnotherDoctorsAppointment()
    {
        await using var f = new ApiTestFactory(); using var doctor = await Doctor(f);
        await f.WithDbContextAsync(async db =>
        {
            var other = new Doctor { ClinicId = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "Outro", Crm = "OUTRO", Email = "outro@example.test" };
            db.Doctors.Add(other);
            var appointment = await db.Appointments.IgnoreQueryFilters().SingleAsync(x => x.Id == Guid.Parse(Appointment));
            appointment.DoctorId = other.Id;
            await db.SaveChangesAsync();
        });
        (await doctor.GetAsync($"/appointments/{Appointment}/clinical-entries")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await doctor.PostAsJsonAsync($"/appointments/{Appointment}/clinical-entries", new CreateClinicalEntryRequest(Guid.NewGuid(), "Document", null, "Documento", "Teste", null))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
