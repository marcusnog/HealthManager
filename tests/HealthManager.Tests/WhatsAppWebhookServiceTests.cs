using FluentAssertions;
using HealthManager.Application;
using HealthManager.Domain;
using HealthManager.Infrastructure;
using HealthManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests;

public sealed class WhatsAppWebhookServiceTests
{
    [Fact]
    public async Task ProcessAsync_ShouldConfirmAppointment_WhenMessageContainsConfirm()
    {
        var clinicId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var appointmentId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

        await using var dbContext = CreateDbContext();
        dbContext.Clinics.Add(new Clinic { Id = clinicId, Name = "Clinica Aurora", Slug = "clinica-aurora" });
        dbContext.Patients.Add(new Patient
        {
            Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            ClinicId = clinicId,
            Name = "Paciente Demo",
            Cpf = "11111111111",
            Phone = "11999999999"
        });
        dbContext.Doctors.Add(new Doctor
        {
            Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            ClinicId = clinicId,
            Name = "Dr. Demo",
            Specialty = "Cardio",
            Crm = "123456"
        });
        dbContext.Appointments.Add(new Appointment
        {
            Id = appointmentId,
            ClinicId = clinicId,
            PatientId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            DoctorId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            StartAt = new DateTimeOffset(2026, 5, 10, 13, 0, 0, TimeSpan.Zero),
            EndAt = new DateTimeOffset(2026, 5, 10, 13, 30, 0, TimeSpan.Zero),
            Status = AppointmentStatus.Scheduled,
            ConfirmationStatus = ConfirmationStatus.Pending,
            Type = "Consulta",
            Amount = 100
        });
        await dbContext.SaveChangesAsync();

        var service = new WhatsAppWebhookService(
            dbContext,
            new FakeTenantProvider(clinicId),
            new OutboxService(dbContext));

        await service.ProcessAsync(
            new WhatsAppWebhookRequest(clinicId, appointmentId, "11999998888", "CONFIRMAR CONSULTA", "meta-test-001"),
            CancellationToken.None);

        var appointment = dbContext.Appointments.IgnoreQueryFilters().Single(x => x.Id == appointmentId);
        appointment.Status.Should().Be(AppointmentStatus.Confirmed);
        appointment.ConfirmationStatus.Should().Be(ConfirmationStatus.Confirmed);

        dbContext.WhatsAppMessages.IgnoreQueryFilters().Should().ContainSingle(x =>
            x.ProviderMessageId == "meta-test-001" && x.Direction == MessageDirection.Inbound);

        dbContext.OutboxEvents.IgnoreQueryFilters().Should().ContainSingle(x =>
            x.EventType == "whatsapp.webhook.processed");
    }

    [Fact]
    public async Task ProcessAsync_ShouldCancelAppointment_WhenMessageContainsCancel()
    {
        var clinicId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var appointmentId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

        await using var dbContext = CreateDbContext();
        dbContext.Clinics.Add(new Clinic { Id = clinicId, Name = "Clinica Aurora", Slug = "clinica-aurora" });
        dbContext.Patients.Add(new Patient
        {
            Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            ClinicId = clinicId,
            Name = "Paciente Demo",
            Cpf = "11111111111",
            Phone = "11999999999"
        });
        dbContext.Doctors.Add(new Doctor
        {
            Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            ClinicId = clinicId,
            Name = "Dr. Demo",
            Specialty = "Cardio",
            Crm = "123456"
        });
        dbContext.Appointments.Add(new Appointment
        {
            Id = appointmentId,
            ClinicId = clinicId,
            PatientId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            DoctorId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            StartAt = new DateTimeOffset(2026, 5, 10, 13, 0, 0, TimeSpan.Zero),
            EndAt = new DateTimeOffset(2026, 5, 10, 13, 30, 0, TimeSpan.Zero),
            Status = AppointmentStatus.Scheduled,
            ConfirmationStatus = ConfirmationStatus.Pending,
            Type = "Consulta",
            Amount = 100
        });
        await dbContext.SaveChangesAsync();

        var service = new WhatsAppWebhookService(
            dbContext,
            new FakeTenantProvider(clinicId),
            new OutboxService(dbContext));

        await service.ProcessAsync(
            new WhatsAppWebhookRequest(clinicId, appointmentId, "11999998888", "CANCELAR CONSULTA", "meta-test-002"),
            CancellationToken.None);

        var appointment = dbContext.Appointments.IgnoreQueryFilters().Single(x => x.Id == appointmentId);
        appointment.Status.Should().Be(AppointmentStatus.Cancelled);
        appointment.ConfirmationStatus.Should().Be(ConfirmationStatus.Declined);
    }

    [Fact]
    public async Task ProcessAsync_ShouldStoreMessageAndWebhookEvent_WhenNoAppointmentId()
    {
        var clinicId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        await using var dbContext = CreateDbContext();
        dbContext.Clinics.Add(new Clinic { Id = clinicId, Name = "Clinica Aurora", Slug = "clinica-aurora" });
        await dbContext.SaveChangesAsync();

        var service = new WhatsAppWebhookService(
            dbContext,
            new FakeTenantProvider(clinicId),
            new OutboxService(dbContext));

        await service.ProcessAsync(
            new WhatsAppWebhookRequest(clinicId, null, "11999998888", "Mensagem generica", "meta-test-003"),
            CancellationToken.None);

        dbContext.WebhookEvents.IgnoreQueryFilters().Should().ContainSingle();
        dbContext.WhatsAppMessages.IgnoreQueryFilters().Should().ContainSingle(x =>
            x.ProviderMessageId == "meta-test-003");
        dbContext.OutboxEvents.IgnoreQueryFilters().Should().BeEmpty();
    }

    private static AppDbContext CreateDbContext() => TestHelpers.CreateDbContext();
}
