using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests.Integration;

public sealed class AppointmentsEndpointsTests
{
    [Fact]
    public async Task AppointmentType_ShouldBeCreatedAndUsedByAppointment()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");

        var typeResponse = await client.PostAsJsonAsync("/appointment-types", new { name = "Teleconsulta" });
        typeResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var appointmentType = await typeResponse.Content.ReadFromJsonAsync<AppointmentTypeHttpResponse>();

        var appointmentResponse = await client.PostAsJsonAsync("/appointments", new
        {
            patientId = "dddddddd-dddd-dddd-dddd-dddddddddddd",
            doctorId = "cccccccc-cccc-cccc-cccc-cccccccccccc",
            startAt = "2026-05-08T15:00:00Z",
            durationMinutes = 30,
            appointmentTypeId = appointmentType!.Id,
            amount = 180
        });

        appointmentResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        (await appointmentResponse.Content.ReadFromJsonAsync<AppointmentHttpResponse>())!.Type.Should().Be("Teleconsulta");
        (await client.DeleteAsync($"/appointment-types/{appointmentType.Id}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

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
            appointmentTypeId = "a7000001-0000-0000-0000-000000000001",
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
    public async Task CreateGroupAppointment_ShouldCreateIndependentAppointmentsAndReceivables()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var secondPatientId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        await factory.WithDbContextAsync(async dbContext =>
        {
            dbContext.Patients.Add(new HealthManager.Domain.Patient
            {
                Id = secondPatientId,
                ClinicId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = "Segundo paciente",
                Cpf = "52998224725",
                Phone = "11999998888"
            });
            await dbContext.SaveChangesAsync();
        });

        var response = await client.PostAsJsonAsync("/appointments/group", new
        {
            patientIds = new[] { "dddddddd-dddd-dddd-dddd-dddddddddddd", secondPatientId.ToString() },
            doctorId = "cccccccc-cccc-cccc-cccc-cccccccccccc",
            startAt = "2026-05-08T15:00:00Z",
            durationMinutes = 30,
            appointmentTypeId = "a7000001-0000-0000-0000-000000000001",
            amount = 180
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var appointments = await response.Content.ReadFromJsonAsync<List<AppointmentHttpResponse>>();
        appointments.Should().HaveCount(2);
        appointments!.Select(x => x.AppointmentGroupId).Distinct().Should().ContainSingle().Which.Should().NotBeNull();

        var updateResponse = await client.PatchAsJsonAsync($"/appointments/{appointments[0].Id}", new { durationMinutes = 45 });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK, "members of the same group may overlap");

        await factory.WithDbContextAsync(dbContext =>
        {
            var ids = appointments.Select(x => x.Id).ToList();
            dbContext.Receivables.Count(x => x.AppointmentId.HasValue && ids.Contains(x.AppointmentId.Value)).Should().Be(2);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task CreateGroupAppointment_ShouldRejectExternalConflictWithoutCreatingAppointments()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var secondPatientId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var appointmentCount = 0;
        await factory.WithDbContextAsync(async dbContext =>
        {
            dbContext.Patients.Add(new HealthManager.Domain.Patient
            {
                Id = secondPatientId,
                ClinicId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = "Segundo paciente",
                Cpf = "52998224725",
                Phone = "11999998888"
            });
            await dbContext.SaveChangesAsync();
            appointmentCount = dbContext.Appointments.Count();
        });

        var response = await client.PostAsJsonAsync("/appointments/group", new
        {
            patientIds = new[] { "dddddddd-dddd-dddd-dddd-dddddddddddd", secondPatientId.ToString() },
            doctorId = "cccccccc-cccc-cccc-cccc-cccccccccccc",
            startAt = "2026-05-07T12:10:00Z",
            durationMinutes = 30,
            appointmentTypeId = "a7000001-0000-0000-0000-000000000001",
            amount = 180
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await factory.WithDbContextAsync(dbContext =>
        {
            dbContext.Appointments.Count().Should().Be(appointmentCount);
            return Task.CompletedTask;
        });
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

    [Theory]
    [InlineData("NoShow")]
    [InlineData("Completed")]
    [InlineData("Cancelled")]
    public async Task StatusCorrection_ShouldRestoreAppointmentAndPreserveReceivedAmounts(string status)
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var appointmentId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        await factory.WithDbContextAsync(async db =>
        {
            var receivable = await db.Receivables.SingleAsync(x => x.AppointmentId == appointmentId);
            receivable.ReceivedAmount = 50;
            receivable.Status = HealthManager.Domain.ReceivableStatus.Partial;
            await db.SaveChangesAsync();
        });
        (await client.PatchAsJsonAsync($"/appointments/{appointmentId}/status", new { status })).StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await client.PatchAsJsonAsync($"/appointments/{appointmentId}/status", new { status = "Scheduled" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<AppointmentHttpResponse>())!.Status.Should().Be("Scheduled");
        await factory.WithDbContextAsync(db =>
        {
            var receivable = db.Receivables.Single(x => x.AppointmentId == appointmentId);
            receivable.ReceivedAmount.Should().Be(50);
            receivable.Status.Should().Be(HealthManager.Domain.ReceivableStatus.Partial);
            db.AuditLogs.Count(x => x.EntityId == appointmentId && x.Action == "appointment.status_changed").Should().Be(2);
            return Task.CompletedTask;
        });
        (await client.PatchAsJsonAsync($"/appointments/{appointmentId}/status", new { status = "Scheduled" })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StatusCorrection_ShouldRejectInvalidOrMissingStatusAndUnknownAppointment()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        (await client.PatchAsJsonAsync("/appointments/eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee/status", new { status = 999 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PatchAsJsonAsync("/appointments/eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee/status", new { })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PatchAsJsonAsync($"/appointments/{Guid.NewGuid()}/status", new { status = "Scheduled" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StatusCorrection_ShouldRejectReactivationWhenPatientAlreadyHasAnotherAppointment()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        const string id = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";
        (await client.PostAsync($"/appointments/{id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/appointments", new
        {
            patientId = "dddddddd-dddd-dddd-dddd-dddddddddddd",
            doctorId = "cccccccc-cccc-cccc-cccc-cccccccccccc",
            startAt = "2026-05-07T12:00:00Z", durationMinutes = 30,
            appointmentTypeId = "a7000001-0000-0000-0000-000000000001", amount = 180
        })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.PatchAsJsonAsync($"/appointments/{id}/status", new { status = "Scheduled" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await factory.WithDbContextAsync(db =>
        {
            db.Appointments.Single(x => x.Id == Guid.Parse(id)).Status.Should().Be(HealthManager.Domain.AppointmentStatus.Cancelled);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task DeleteAppointment_ShouldHideAppointmentCancelReceivableAndAudit()
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var id = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        (await client.DeleteAsync($"/appointments/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/appointments/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await factory.WithDbContextAsync(db =>
        {
            db.Appointments.Any(x => x.Id == id).Should().BeFalse();
            db.Appointments.IgnoreQueryFilters().Single(x => x.Id == id).DeletedAt.Should().NotBeNull();
            db.Receivables.Single(x => x.AppointmentId == id).Status.Should().Be(HealthManager.Domain.ReceivableStatus.Cancelled);
            db.AuditLogs.Single(x => x.EntityId == id && x.Action == "appointment.deleted").UserId.Should().Be(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
            return Task.CompletedTask;
        });
        var list = await client.GetFromJsonAsync<HealthManager.Application.PagedResult<AppointmentHttpResponse>>("/appointments?date=2026-05-07");
        list!.Items.Should().NotContain(x => x.Id == id);
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("paid")]
    [InlineData("payment")]
    [InlineData("clinical")]
    [InlineData("checkout")]
    public async Task DeleteAppointment_ShouldRejectProtectedHistoryWithoutChangingData(string protection)
    {
        await using var factory = new ApiTestFactory();
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var id = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        await factory.WithDbContextAsync(async db =>
        {
            var appointment = db.Appointments.Single(x => x.Id == id);
            var receivable = db.Receivables.Single(x => x.AppointmentId == id);
            if (protection is "partial" or "paid") receivable.ReceivedAmount = protection == "paid" ? receivable.OriginalAmount : 50;
            if (protection == "payment") db.Payments.Add(new HealthManager.Domain.Payment { ClinicId = appointment.ClinicId, ReceivableId = receivable.Id, Amount = 50 });
            if (protection == "clinical") db.ClinicalRecords.Add(new HealthManager.Domain.ClinicalRecord { ClinicId = appointment.ClinicId, AppointmentId = id, PatientId = appointment.PatientId, DoctorId = appointment.DoctorId });
            if (protection == "checkout") db.PaymentIntents.Add(new HealthManager.Domain.PaymentIntent { ClinicId = appointment.ClinicId, ReceivableId = receivable.Id, Amount = 50, IdempotencyKey = "delete-protection", Status = HealthManager.Domain.PaymentIntentStatus.Processing });
            await db.SaveChangesAsync();
        });
        (await client.DeleteAsync($"/appointments/{id}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await factory.WithDbContextAsync(db =>
        {
            db.Appointments.Single(x => x.Id == id).DeletedAt.Should().BeNull();
            db.Receivables.Single(x => x.AppointmentId == id).Status.Should().Be(HealthManager.Domain.ReceivableStatus.Pending);
            db.AuditLogs.Any(x => x.EntityId == id && x.Action == "appointment.deleted").Should().BeFalse();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task DeleteAppointment_ShouldRequireAuthenticationAndIsolateClinic()
    {
        await using var factory = new ApiTestFactory();
        using var anonymous = factory.CreateClient();
        (await anonymous.DeleteAsync("/appointments/eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var client = await factory.CreateAuthenticatedClientAsync("admin@clinicaaurora.com", "ChangeMe123!");
        var id = Guid.NewGuid();
        var clinicId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        await factory.SeedSecondClinicPatientAsync();
        await factory.WithDbContextAsync(async db =>
        {
            var doctor = new HealthManager.Domain.Doctor { ClinicId = clinicId, Name = "Outro medico", Crm = "OUTRO" };
            var type = new HealthManager.Domain.AppointmentType { ClinicId = clinicId, Name = "Consulta" };
            db.Doctors.Add(doctor);
            db.AppointmentTypes.Add(type);
            db.Appointments.Add(new HealthManager.Domain.Appointment { Id = id, ClinicId = clinicId, DoctorId = doctor.Id, PatientId = Guid.Parse("99999999-9999-9999-9999-999999999999"), AppointmentTypeId = type.Id, StartAt = DateTimeOffset.UtcNow, EndAt = DateTimeOffset.UtcNow.AddMinutes(30) });
            await db.SaveChangesAsync();
        });
        (await client.DeleteAsync($"/appointments/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await factory.WithDbContextAsync(db =>
        {
            db.Appointments.IgnoreQueryFilters().Single(x => x.Id == id).DeletedAt.Should().BeNull();
            return Task.CompletedTask;
        });
    }

    private sealed record AppointmentHttpResponse(
        Guid Id,
        Guid? AppointmentGroupId,
        Guid PatientId,
        Guid DoctorId,
        DateTimeOffset StartAt,
        DateTimeOffset EndAt,
        string Status,
        string ConfirmationStatus,
        string Type,
        decimal Amount,
        string? Notes);

    private sealed record AppointmentTypeHttpResponse(Guid Id, string Name);
}
