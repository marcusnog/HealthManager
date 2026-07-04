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
        var startAt = new DateTimeOffset(2026, 5, 10, 13, 0, 0, TimeSpan.Zero);

        await using var dbContext = CreateDbContext();
        dbContext.Clinics.Add(new Clinic { Id = clinicId, Name = "Clinica A", Slug = "clinica-a", Timezone = "America/Sao_Paulo" });
        dbContext.Doctors.Add(new Doctor { Id = doctorId, ClinicId = clinicId, Name = "Dr. A", Specialty = "Cardio", Crm = "123" });
        dbContext.Patients.AddRange(
            new Patient { Id = patientA, ClinicId = clinicId, Name = "Paciente A", Cpf = "11111111111", Phone = "11999999999" },
            new Patient { Id = patientB, ClinicId = clinicId, Name = "Paciente B", Cpf = "22222222222", Phone = "11888888888" });
        dbContext.Appointments.Add(new Appointment
        {
            ClinicId = clinicId,
            PatientId = patientA,
            DoctorId = doctorId,
            StartAt = startAt,
            EndAt = startAt.AddMinutes(30),
            Type = "Consulta",
            Amount = 100
        });
        await dbContext.SaveChangesAsync();

        var service = new AppointmentService(
            dbContext,
            new FakeTenantProvider(clinicId),
            new OutboxService(dbContext));

        var action = async () => await service.CreateAsync(
            new CreateAppointmentRequest(patientB, doctorId, startAt.AddMinutes(10), 30, null, "Consulta", 150),
            CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Conflito de horario*");
    }

    private static AppDbContext CreateDbContext() => TestHelpers.CreateDbContext();
}
