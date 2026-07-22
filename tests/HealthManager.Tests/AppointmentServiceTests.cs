using FluentAssertions;
using HealthManager.Application;
using HealthManager.Domain;
using HealthManager.Infrastructure;
using HealthManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests;

public sealed class AppointmentServiceTests
{
    [Fact]
    public async Task CreateAsync_ShouldRejectConflictingAppointment()
    {
        var clinicId = Guid.NewGuid();
        var doctorId = Guid.NewGuid();
        var patientA = Guid.NewGuid();
        var patientB = Guid.NewGuid();
        var appointmentTypeId = Guid.NewGuid();
        var startAt = new DateTimeOffset(2026, 5, 10, 13, 0, 0, TimeSpan.Zero);

        await using var dbContext = CreateDbContext();
        dbContext.Clinics.Add(new Clinic { Id = clinicId, Name = "Clinica A", Slug = "clinica-a", Timezone = "America/Sao_Paulo" });
        dbContext.Doctors.Add(new Doctor { Id = doctorId, ClinicId = clinicId, Name = "Dr. A", Crm = "123" });
        dbContext.Patients.AddRange(
            new Patient { Id = patientA, ClinicId = clinicId, Name = "Paciente A", Cpf = "11111111111", Phone = "11999999999" },
            new Patient { Id = patientB, ClinicId = clinicId, Name = "Paciente B", Cpf = "22222222222", Phone = "11888888888" });
        dbContext.AppointmentTypes.Add(new AppointmentType { Id = appointmentTypeId, ClinicId = clinicId, Name = "Consulta" });
        dbContext.Appointments.Add(new Appointment
        {
            ClinicId = clinicId,
            PatientId = patientA,
            DoctorId = doctorId,
            StartAt = startAt,
            EndAt = startAt.AddMinutes(30),
            AppointmentTypeId = appointmentTypeId,
            Amount = 100
        });
        await dbContext.SaveChangesAsync();

        var service = new AppointmentService(
            dbContext,
            new FakeTenantProvider(clinicId),
            new OutboxService(dbContext));

        var action = async () => await service.CreateAsync(
            new CreateAppointmentRequest(patientB, doctorId, startAt.AddMinutes(10), 30, null, appointmentTypeId, 150),
            CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Conflito de horario*");
    }

    [Fact]
    public async Task UpdateAsync_ShouldCreateAndSynchronizeReceivable()
    {
        var clinicId = Guid.NewGuid();
        var doctorId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        var returnTypeId = Guid.NewGuid();
        var consultationTypeId = Guid.NewGuid();
        var startAt = new DateTimeOffset(2026, 5, 11, 13, 0, 0, TimeSpan.Zero);

        await using var dbContext = CreateDbContext();
        dbContext.Clinics.Add(new Clinic { Id = clinicId, Name = "Clinica A", Slug = "clinica-a", Timezone = "America/Sao_Paulo" });
        dbContext.Doctors.Add(new Doctor { Id = doctorId, ClinicId = clinicId, Name = "Dr. A", Crm = "123" });
        dbContext.Patients.Add(new Patient { Id = patientId, ClinicId = clinicId, Name = "Paciente A", Cpf = "11111111111", Phone = "11999999999" });
        dbContext.AppointmentTypes.AddRange(
            new AppointmentType { Id = returnTypeId, ClinicId = clinicId, Name = "Retorno" },
            new AppointmentType { Id = consultationTypeId, ClinicId = clinicId, Name = "Consulta" });
        dbContext.Appointments.Add(new Appointment
        {
            Id = appointmentId,
            ClinicId = clinicId,
            PatientId = patientId,
            DoctorId = doctorId,
            StartAt = startAt,
            EndAt = startAt.AddMinutes(30),
            AppointmentTypeId = returnTypeId,
            Amount = 0
        });
        await dbContext.SaveChangesAsync();

        var service = new AppointmentService(dbContext, new FakeTenantProvider(clinicId), new OutboxService(dbContext));
        var newStartAt = startAt.AddDays(1);
        await service.UpdateAsync(
            appointmentId,
            new UpdateAppointmentRequest(null, newStartAt, null, null, consultationTypeId, 180),
            CancellationToken.None);

        var receivable = dbContext.Receivables.Single();
        receivable.AppointmentId.Should().Be(appointmentId);
        receivable.OriginalAmount.Should().Be(180);
        receivable.DueDate.Should().Be(newStartAt);
        receivable.Description.Should().Be("Consulta Consulta");
        receivable.Status.Should().Be(ReceivableStatus.Pending);

        var financial = await new FinancialService(dbContext, new FakeTenantProvider(clinicId))
            .ListReceivablesAsync(new FinancialQuery(), CancellationToken.None);
        financial.Items.Single().AppointmentStartAt.Should().Be(newStartAt);
        financial.Items.Single().AppointmentType.Should().Be("Consulta");
        financial.Items.Single().DoctorName.Should().Be("Dr. A");
    }

    private static AppDbContext CreateDbContext() => TestHelpers.CreateDbContext();
}
