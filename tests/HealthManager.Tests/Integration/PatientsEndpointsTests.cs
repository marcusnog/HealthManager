using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests.Integration;

public sealed class PatientsEndpointsTests
{
    [Fact]
    public async Task ListPatients_ShouldOnlyReturnCurrentClinicPatients()
    {
        await using var factory = new ApiTestFactory();
        await factory.SeedSecondClinicPatientAsync();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var response = await client.GetAsync("/patients?page=1&pageSize=50");
        var body = await response.Content.ReadAsStringAsync();
        var authHeader = string.Join(" | ", response.Headers.WwwAuthenticate.Select(x => x.ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"{authHeader} {body}");
        var payload = await response.Content.ReadFromJsonAsync<PagedPatientsHttpResponse>();

        payload.Should().NotBeNull();
        payload!.Items.Should().Contain(x => x.Name == "Ana Martins");
        payload.Items.Should().NotContain(x => x.Name == "Paciente de Outro Tenant");
    }

    [Fact]
    public async Task ListPatients_ShouldFilterBySearchTerm()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var response = await client.GetAsync("/patients?page=1&pageSize=20&search=Ana");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<PagedPatientsHttpResponse>();

        payload.Should().NotBeNull();
        payload!.Items.Should().ContainSingle(x => x.Name == "Ana Martins");
    }

    [Fact]
    public async Task UpdatePatient_ShouldPersistChangesForCurrentClinicPatient()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var patientId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        var updateResponse = await client.PatchAsJsonAsync($"/patients/{patientId}", new
        {
            name = "Ana Martins Atualizada",
            phone = "11988887777",
            email = "ana.atualizada@email.com",
            healthInsurance = "Particular",
            notes = "Cadastro ajustado no teste."
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await updateResponse.Content.ReadFromJsonAsync<PatientHttpResponse>();

        payload.Should().NotBeNull();
        payload!.Name.Should().Be("Ana Martins Atualizada");
        payload.Phone.Should().Be("11988887777");
        payload.Email.Should().Be("ana.atualizada@email.com");
        payload.HealthInsurance.Should().Be("Particular");
    }

    [Fact]
    public async Task PatientDocuments_ShouldAllowAddingAndListingDocumentsForCurrentClinicPatient()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var patientId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        using var multipart = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent("pedido-exame-pdf"u8.ToArray());
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        multipart.Add(fileContent, "file", "pedido-exame.pdf");

        var createResponse = await client.PostAsync($"/patients/{patientId}/documents/upload", multipart);
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listResponse = await client.GetAsync($"/patients/{patientId}/documents");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await listResponse.Content.ReadFromJsonAsync<List<PatientDocumentHttpResponse>>();

        payload.Should().NotBeNull();
        payload!.Should().ContainSingle();
        payload[0].FileName.Should().Be("pedido-exame.pdf");
        payload[0].ContentType.Should().Be("application/pdf");
        payload[0].StoragePath.Should().Contain($"clinics/11111111-1111-1111-1111-111111111111/patients/{patientId}");
    }

    [Fact]
    public async Task PatientDocumentsUpload_ShouldPersistUploadedDocumentForCurrentClinicPatient()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var patientId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        using var multipart = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent("pdf-demo"u8.ToArray());
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        multipart.Add(fileContent, "file", "laudo-cardiologia.pdf");

        var response = await client.PostAsync($"/patients/{patientId}/documents/upload", multipart);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<PatientDocumentHttpResponse>();
        payload.Should().NotBeNull();
        payload!.FileName.Should().Be("laudo-cardiologia.pdf");
        payload.ContentType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task PatientDocumentsDownload_ShouldReturnUploadedDocumentContent()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var patientId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        using (var multipart = new MultipartFormDataContent())
        {
            using var fileContent = new ByteArrayContent("pdf-demo"u8.ToArray());
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
            multipart.Add(fileContent, "file", "laudo-download.pdf");

            var uploadResponse = await client.PostAsync($"/patients/{patientId}/documents/upload", multipart);
            uploadResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var uploadPayload = await uploadResponse.Content.ReadFromJsonAsync<PatientDocumentHttpResponse>();
            uploadPayload.Should().NotBeNull();

            var downloadResponse = await client.GetAsync($"/patients/{patientId}/documents/{uploadPayload!.Id}/download");
            downloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            downloadResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");

            var bytes = await downloadResponse.Content.ReadAsByteArrayAsync();
            bytes.Should().Equal("pdf-demo"u8.ToArray());
        }
    }

    [Fact]
    public async Task PatientDocumentsDelete_ShouldSoftDeleteDocumentAndHideItFromListings()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var patientId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        using var multipart = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent("pdf-demo"u8.ToArray());
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        multipart.Add(fileContent, "file", "laudo-delete.pdf");

        var uploadResponse = await client.PostAsync($"/patients/{patientId}/documents/upload", multipart);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var uploadPayload = await uploadResponse.Content.ReadFromJsonAsync<PatientDocumentHttpResponse>();
        uploadPayload.Should().NotBeNull();

        var deleteResponse = await client.DeleteAsync($"/patients/{patientId}/documents/{uploadPayload!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listResponse = await client.GetAsync($"/patients/{patientId}/documents");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listedDocuments = await listResponse.Content.ReadFromJsonAsync<List<PatientDocumentHttpResponse>>();
        listedDocuments.Should().NotBeNull();
        listedDocuments!.Should().NotContain(x => x.Id == uploadPayload.Id);

        var downloadResponse = await client.GetAsync($"/patients/{patientId}/documents/{uploadPayload.Id}/download");
        downloadResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await factory.WithDbContextAsync(async dbContext =>
        {
            var deletedDocument = await dbContext.PatientDocuments
                .IgnoreQueryFilters()
                .FirstAsync(x => x.Id == uploadPayload.Id);

            deletedDocument.DeletedAt.Should().NotBeNull();

            dbContext.AuditLogs
                .IgnoreQueryFilters()
                .Should()
                .Contain(x => x.EntityId == uploadPayload.Id && x.Action == "patient_document.deleted");
        });
    }

    private sealed record PagedPatientsHttpResponse(List<PatientHttpResponse> Items, int Page, int PageSize, int Total);
    private sealed record PatientHttpResponse(Guid Id, string Name, string Cpf, string Phone, string? Email, string? HealthInsurance);
    private sealed record PatientDocumentHttpResponse(Guid Id, string FileName, string ContentType, long SizeInBytes, string StoragePath);
}
