using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace HealthManager.Tests.Integration;

public sealed class ClinicalRecordsEndpointsTests
{
    private static readonly Guid AppointmentId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid PatientId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid DoctorUserId = Guid.Parse("a1a2a3a4-a1a2-a1a2-a1a2-a1a2a3a4a5a6");

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
    public async Task Secretary_ShouldAddAddendum_ToFinalizedRecord()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

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
