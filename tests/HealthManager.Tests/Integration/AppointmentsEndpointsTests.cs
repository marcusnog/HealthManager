using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests.Integration;

public sealed class AppointmentsEndpointsTests
{
    [Fact]
    public async Task CreateAppointment_ShouldReturnBadRequest_WhenThereIsConflict()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var response = await client.PostAsJsonAsync("/appointments", new
        {
            patientId = "dddddddd-dddd-dddd-dddd-dddddddddddd",
            doctorId = "cccccccc-cccc-cccc-cccc-cccccccccccc",
            startAt = "2026-05-07T12:10:00Z",
            durationMinutes = 30,
            notes = "Tentativa em horario conflitante",
            type = "Retorno",
            amount = 180
        });
        var body = await response.Content.ReadAsStringAsync();
        var authHeader = string.Join(" | ", response.Headers.WwwAuthenticate.Select(x => x.ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"{authHeader} {body}");
        var payload = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        payload.Should().NotBeNull();
        payload!.Detail.Should().Contain("Conflito de horario");
    }

    [Fact]
    public async Task ConfirmAppointment_ShouldUpdateStatusAndConfirmationStatus()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var appointmentId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

        var response = await client.PostAsync($"/appointments/{appointmentId}/confirm", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<AppointmentHttpResponse>();
        payload.Should().NotBeNull();
        payload!.Status.Should().Be("Confirmed");
        payload.ConfirmationStatus.Should().Be("Confirmed");
    }

    [Fact]
    public async Task CancelAppointment_ShouldUpdateStatusAndKeepOutboxConsistent()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var appointmentId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

        var response = await client.PostAsync($"/appointments/{appointmentId}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<AppointmentHttpResponse>();
        payload.Should().NotBeNull();
        payload!.Status.Should().Be("Cancelled");

        await factory.WithDbContextAsync(dbContext =>
        {
            dbContext.OutboxEvents
                .IgnoreQueryFilters()
                .Should()
                .Contain(x => x.EventType == "appointment.cancelled");
            return Task.CompletedTask;
        });
    }

    private sealed record AppointmentHttpResponse(
        Guid Id,
        Guid PatientId,
        Guid DoctorId,
        DateTimeOffset StartAt,
        DateTimeOffset EndAt,
        string Status,
        string ConfirmationStatus,
        string Type,
        decimal Amount,
        string? Notes);
}
