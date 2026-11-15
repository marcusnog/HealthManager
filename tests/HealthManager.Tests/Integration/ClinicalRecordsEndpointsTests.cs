using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests.Integration;

public sealed class ClinicalRecordsEndpointsTests
{
    private static readonly Guid AppointmentId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid PatientId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid DoctorUserId = Guid.Parse("a1a2a3a4-a1a2-a1a2-a1a2-a1a2a3a4a5a6");

    [Fact]
    public async Task Owner_CanReopenEditAndFinalize_PreservingSnapshotAddendumsAndDocuments()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("henrique.lima@clinicaaurora.com", "ChangeMe123!");
        var url = $"/appointments/{AppointmentId}/clinical-record";
        (await client.PostAsync($"{url}/reopen", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync(url, new { content = "Original", prescription = "Receita original", chiefComplaint = "Queixa" })).EnsureSuccessStatusCode();
        (await client.PostAsync($"{url}/reopen", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var entryUrl = $"/appointments/{AppointmentId}/clinical-entries";
        var entry = new HealthManager.Application.CreateClinicalEntryRequest(Guid.NewGuid(), "Document", null, "Receita", "Documento original", null);
        (await client.PostAsJsonAsync(entryUrl, entry)).EnsureSuccessStatusCode();
        var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        var finalized = await client.PostAsync($"{url}/finalize", null);
        finalized.EnsureSuccessStatusCode();
        var snapshot = (await finalized.Content.ReadFromJsonAsync<HealthManager.Application.ClinicalRecordResponse>(json))!;
        (await client.PostAsJsonAsync($"{url}/addendum", new { content = "Adendo original" })).EnsureSuccessStatusCode();
        var reopened = await client.PostAsync($"{url}/reopen", null);
        reopened.EnsureSuccessStatusCode();
        var draft = (await reopened.Content.ReadFromJsonAsync<HealthManager.Application.ClinicalRecordResponse>(json))!;
        draft.Status.Should().Be(ClinicalRecordStatus.Draft);
        draft.FinalizedAt.Should().BeNull();
        draft.Content.Should().Be(snapshot.Content);
        draft.Prescription.Should().Be(snapshot.Prescription);
        (await client.PostAsync($"{url}/reopen", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PatchAsJsonAsync(url, new { content = "Revisado" })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(entryUrl, entry with { ClientRequestId = Guid.NewGuid(), Content = "Novo documento" })).EnsureSuccessStatusCode();
        (await client.PostAsync($"{url}/finalize", null)).EnsureSuccessStatusCode();
        await factory.WithDbContextAsync(async db => {
            var audit = await db.AuditLogs.SingleAsync(x => x.Action == "clinical_record.reopened");
            audit.UserId.Should().Be(DoctorUserId);
            audit.ClinicId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
            audit.EntityId.Should().Be(snapshot.Id);
            System.Text.Json.JsonSerializer.Deserialize<HealthManager.Application.ClinicalRecordResponse>(audit.PayloadJson).Should().BeEquivalentTo(snapshot);
            (await db.ClinicalRecordAddendums.SingleAsync()).Content.Should().Be("Adendo original");
            (await db.ClinicalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(2);
        });
    }

    [Fact]
    public async Task UnlinkedDoctor_ShouldReceiveForbidden_AndKeepValidSession()
    {
        await using var factory = new ApiTestFactory();
        await factory.WithDbContextAsync(async db =>
        {
            var owner = await db.Users.SingleAsync(x => x.Id == DoctorUserId);
            db.Users.Add(new User { ClinicId = owner.ClinicId, Name = "Medico sem vinculo",
                Email = "sem.vinculo@clinicaaurora.com", PasswordHash = owner.PasswordHash, Role = UserRole.Doctor });
            await db.SaveChangesAsync();
        });
        using var client = await factory.CreateAuthenticatedClientAsync("sem.vinculo@clinicaaurora.com", "ChangeMe123!");
        var response = await client.GetAsync($"/patients/{PatientId}/clinical-records");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("mesmo e-mail");
        var patients = await client.GetAsync("/patients");
        patients.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await client.GetAsync($"/patients/{PatientId}/clinical-entries");
        history.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("henrique.lima@clinicaaurora.com")]
    [InlineData("admin@clinicaaurora.com")]
    public async Task Staff_ShouldSaveReadAndClearFreeText_WithoutChangingLegacyFields(string email)
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync(email, "ChangeMe123!");
        var url = $"/appointments/{AppointmentId}/clinical-record";
        var created = await client.PostAsJsonAsync(url, new
        {
            chiefComplaint = "Queixa anterior", content = "Texto livre\nSegunda linha", prescription = "Produto — orientacoes"
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var saved = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(url);
        saved.GetProperty("content").GetString().Should().Be("Texto livre\nSegunda linha");
        saved.GetProperty("prescription").GetString().Should().Be("Produto — orientacoes");
        var updated = await client.PatchAsJsonAsync(url, new { content = "Revisado", prescription = "" });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        saved = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(url);
        saved.GetProperty("content").GetString().Should().Be("Revisado");
        saved.GetProperty("prescription").GetString().Should().Be("");
        saved.GetProperty("chiefComplaint").GetString().Should().Be("Queixa anterior");
        using var doctor = await factory.CreateAuthenticatedClientAsync("henrique.lima@clinicaaurora.com", "ChangeMe123!");
        (await doctor.PostAsync($"{url}/finalize", null)).EnsureSuccessStatusCode();
        var rejected = await client.PatchAsJsonAsync(url, new { content = "Alteracao", prescription = "Alteracao" });
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Doctor_ShouldCreateDraft_WhenStartingConsultation()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("henrique.lima@clinicaaurora.com", "ChangeMe123!");

        var response = await client.PostAsJsonAsync($"/appointments/{AppointmentId}/clinical-record", new
        {
            chiefComplaint = "Dor no peito",
            history = "Paciente relata dor retroesternal ha 2 horas",
            physicalExam = "PA: 130x85, FC: 88bpm",
            assessment = "Suspeita de dor toracica nao cardiaca",
            plan = "ECG, troponinas, observacao"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<ClinicalRecordHttpResponse>();
        body!.Status.Should().Be("Draft");
        body.ChiefComplaint.Should().Be("Dor no peito");
        body.PatientId.Should().Be(PatientId);
    }

    [Fact]
    public async Task Doctor_ShouldUpdateDraft_BeforeFinalization()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("henrique.lima@clinicaaurora.com", "ChangeMe123!");

        await client.PostAsJsonAsync($"/appointments/{AppointmentId}/clinical-record", new
        {
            chiefComplaint = "Dor no peito"
        });

        var updateResponse = await client.PatchAsJsonAsync($"/appointments/{AppointmentId}/clinical-record", new
        {
            assessment = "Dor musculoesqueletica"
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await updateResponse.Content.ReadFromJsonAsync<ClinicalRecordHttpResponse>();
        body!.Assessment.Should().Be("Dor musculoesqueletica");
        body.ChiefComplaint.Should().Be("Dor no peito");
    }

    [Fact]
    public async Task Doctor_ShouldFinalizeRecord()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("henrique.lima@clinicaaurora.com", "ChangeMe123!");

        await client.PostAsJsonAsync($"/appointments/{AppointmentId}/clinical-record", new
        {
            chiefComplaint = "Dor no peito"
        });

        var finalizeResponse = await client.PostAsync($"/appointments/{AppointmentId}/clinical-record/finalize", null);
        finalizeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await finalizeResponse.Content.ReadFromJsonAsync<ClinicalRecordHttpResponse>();
        body!.Status.Should().Be("Finalized");
        body.FinalizedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Doctor_ShouldNotEditRecord_AfterFinalization()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("henrique.lima@clinicaaurora.com", "ChangeMe123!");

        await client.PostAsJsonAsync($"/appointments/{AppointmentId}/clinical-record", new
        {
            chiefComplaint = "Dor no peito"
        });
        await client.PostAsync($"/appointments/{AppointmentId}/clinical-record/finalize", null);

        var updateResponse = await client.PatchAsJsonAsync($"/appointments/{AppointmentId}/clinical-record", new
        {
            assessment = "Tentativa de alteracao"
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Doctor_ShouldAddAddendum_ToFinalizedRecord()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("henrique.lima@clinicaaurora.com", "ChangeMe123!");

        await client.PostAsJsonAsync($"/appointments/{AppointmentId}/clinical-record", new
        {
            chiefComplaint = "Dor no peito"
        });
        await client.PostAsync($"/appointments/{AppointmentId}/clinical-record/finalize", null);

        var addendumResponse = await client.PostAsJsonAsync($"/appointments/{AppointmentId}/clinical-record/addendum", new
        {
            content = "Paciente retornou para revisao. Evolucao favoravel."
        });

        addendumResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await addendumResponse.Content.ReadFromJsonAsync<AddendumHttpResponse>();
        body!.Content.Should().Be("Paciente retornou para revisao. Evolucao favoravel.");
    }

    [Fact]
    public async Task Admin_ShouldCreateDraft_PreservingDoctor_WithoutFinalizationPermission()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var response = await client.PostAsJsonAsync($"/appointments/{AppointmentId}/clinical-record", new
        {
            chiefComplaint = "Tentativa sem medico"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var record = await response.Content.ReadFromJsonAsync<ClinicalRecordHttpResponse>();
        record!.DoctorId.Should().Be(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        (await client.PostAsync($"/appointments/{AppointmentId}/clinical-record/finalize", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync($"/appointments/{AppointmentId}/clinical-record/reopen", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync($"/appointments/{AppointmentId}/clinical-record/addendum", new { content = "Adendo" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Doctor_ShouldNotAddAddendum_ToDraftRecord()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("henrique.lima@clinicaaurora.com", "ChangeMe123!");
        await client.PostAsJsonAsync($"/appointments/{AppointmentId}/clinical-record", new { chiefComplaint = "Dor" });

        var response = await client.PostAsJsonAsync($"/appointments/{AppointmentId}/clinical-record/addendum", new { content = "Correcao" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(UserRole.Secretary)]
    [InlineData(UserRole.Doctor)]
    public async Task OtherStaff_ShouldNotCreateOrEditClinicalContent(UserRole role)
    {
        await using var factory = new ApiTestFactory();
        await factory.WithDbContextAsync(async db =>
        {
            var owner = await db.Users.SingleAsync(x => x.Id == DoctorUserId);
            db.Users.Add(new User { ClinicId = owner.ClinicId, Name = "Outro usuario",
                Email = "outro@clinicaaurora.com", PasswordHash = owner.PasswordHash, Role = role });
            if (role == UserRole.Doctor)
                db.Doctors.Add(new Doctor { ClinicId = owner.ClinicId, Name = "Outro medico", Email = "outro@clinicaaurora.com", Crm = "CRM-SP-123456" });
            await db.SaveChangesAsync();
        });
        using var client = await factory.CreateAuthenticatedClientAsync("outro@clinicaaurora.com", "ChangeMe123!");
        var url = $"/appointments/{AppointmentId}/clinical-record";
        (await client.PostAsJsonAsync(url, new { content = "Texto", prescription = "Receita" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var admin = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        (await admin.PostAsJsonAsync(url, new { content = "Texto" })).EnsureSuccessStatusCode();
        (await client.PatchAsJsonAsync(url, new { content = "Alterado", prescription = "Receita" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var owner = await factory.CreateAuthenticatedClientAsync("henrique.lima@clinicaaurora.com", "ChangeMe123!");
        (await owner.PostAsync($"{url}/finalize", null)).EnsureSuccessStatusCode();
        (await client.PostAsync($"{url}/reopen", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync($"/appointments/{AppointmentId}/clinical-entries", new HealthManager.Application.CreateClinicalEntryRequest(Guid.NewGuid(), "Document", null, "Receita", "Texto", null))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_ShouldNotWriteAnotherClinicsClinicalContent()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        await factory.SeedSecondClinicPatientAsync();
        var foreignAppointmentId = Guid.NewGuid();
        var foreignClinicId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        await factory.WithDbContextAsync(async db =>
        {
            var appointment = new Appointment { Id = foreignAppointmentId, ClinicId = foreignClinicId,
                PatientId = Guid.Parse("99999999-9999-9999-9999-999999999999"), DoctorId = Guid.NewGuid() };
            db.Appointments.Add(appointment);
            db.ClinicalRecords.Add(new ClinicalRecord { ClinicId = foreignClinicId, AppointmentId = appointment.Id,
                PatientId = appointment.PatientId, DoctorId = appointment.DoctorId, Content = "Privado" });
            await db.SaveChangesAsync();
        });
        var url = $"/appointments/{foreignAppointmentId}/clinical-record";
        (await client.PostAsJsonAsync(url, new { content = "Texto" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PatchAsJsonAsync(url, new { prescription = "Receita" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var doctor = await factory.CreateAuthenticatedClientAsync("henrique.lima@clinicaaurora.com", "ChangeMe123!");
        (await doctor.PostAsync($"{url}/reopen", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync($"/appointments/{foreignAppointmentId}/clinical-entries", new HealthManager.Application.CreateClinicalEntryRequest(Guid.NewGuid(), "Document", null, "Receita", "Texto", null))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AnotherDoctor_ShouldNotReadAddendums()
    {
        await using var factory = new ApiTestFactory();
        using var ownerClient = await factory.CreateAuthenticatedClientAsync("henrique.lima@clinicaaurora.com", "ChangeMe123!");
        await ownerClient.PostAsJsonAsync($"/appointments/{AppointmentId}/clinical-record", new { chiefComplaint = "Dor" });
        await ownerClient.PostAsync($"/appointments/{AppointmentId}/clinical-record/finalize", null);

        await factory.WithDbContextAsync(async dbContext =>
        {
            var owner = await dbContext.Users.SingleAsync(x => x.Email == "henrique.lima@clinicaaurora.com");
            dbContext.Users.Add(new User
            {
                ClinicId = owner.ClinicId,
                Name = "Dra. Outra Medica",
                Email = "outra.medica@clinicaaurora.com",
                PasswordHash = owner.PasswordHash,
                Role = UserRole.Doctor
            });
            dbContext.Doctors.Add(new Doctor
            {
                ClinicId = owner.ClinicId,
                Name = "Dra. Outra Medica",
                Email = "outra.medica@clinicaaurora.com",
                Crm = "CRM-SP-654321"
            });
            await dbContext.SaveChangesAsync();
        });

        using var otherDoctorClient = await factory.CreateAuthenticatedClientAsync("outra.medica@clinicaaurora.com", "ChangeMe123!");
        var response = await otherDoctorClient.GetAsync($"/appointments/{AppointmentId}/clinical-record/addendums");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ShouldReturnNotFound_WhenNoRecordExists()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("henrique.lima@clinicaaurora.com", "ChangeMe123!");

        var response = await client.GetAsync($"/appointments/{AppointmentId}/clinical-record");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Doctor_ShouldListRecords_ByPatient()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("henrique.lima@clinicaaurora.com", "ChangeMe123!");

        await client.PostAsJsonAsync($"/appointments/{AppointmentId}/clinical-record", new
        {
            chiefComplaint = "Dor no peito"
        });

        var response = await client.GetAsync($"/patients/{PatientId}/clinical-records");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<ClinicalRecordHttpResponse>>();
        body.Should().NotBeEmpty();
    }

    private sealed record ClinicalRecordHttpResponse(
        Guid Id, Guid AppointmentId, Guid PatientId, Guid DoctorId,
        string Status, string? ChiefComplaint, string? History,
        string? PhysicalExam, string? Assessment, string? Plan,
        DateTimeOffset? FinalizedAt);

    private sealed record AddendumHttpResponse(Guid Id, string Content, Guid AuthorId, DateTimeOffset CreatedAt);
}
