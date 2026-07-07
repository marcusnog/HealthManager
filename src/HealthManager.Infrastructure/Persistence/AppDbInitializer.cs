using HealthManager.Application;
using HealthManager.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HealthManager.Infrastructure.Persistence;

public static class AppDbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AppDbInitializer");
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (dbContext.Database.IsRelational())
        {
            logger.LogInformation("Applying database migrations.");
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
        else
        {
            logger.LogInformation("Ensuring database is created for non-relational provider.");
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }

        if (await dbContext.Users.IgnoreQueryFilters().AnyAsync(cancellationToken))
        {
            logger.LogInformation("Seed skipped because data already exists.");
            return;
        }

        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = DateTimeOffset.UtcNow;
        var clinicId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var platformAdminId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var clinicAdminId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var doctorId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var patientId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var appointmentId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var receivableId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

        var clinic = new Clinic
        {
            Id = clinicId,
            Name = "Clinica Aurora",
            Slug = "clinica-aurora",
            Timezone = "America/Sao_Paulo",
            BusinessHoursJson = "{\"start\":\"08:00\",\"end\":\"18:00\"}",
            Email = "contato@clinicaaurora.com",
            Phone = "1133334444",
            Address = "Av. Paulista, 1000 - Sao Paulo",
            CreatedAt = now,
            UpdatedAt = now
        };

        var platformAdmin = new User
        {
            Id = platformAdminId,
            Name = "Platform Admin",
            Email = "platform@healthmanager.local",
            PasswordHash = passwordHasher.Hash("ChangeMe123!"),
            Role = UserRole.PlatformAdmin,
            ClinicId = null,
            CreatedAt = now,
            UpdatedAt = now
        };

        var clinicAdmin = new User
        {
            Id = clinicAdminId,
            ClinicId = clinicId,
            Name = "Camila Rocha",
            Email = "admin@clinicaaurora.com",
            PasswordHash = passwordHasher.Hash("ChangeMe123!"),
            Role = UserRole.Admin,
            CreatedAt = now,
            UpdatedAt = now
        };

        var doctorUserId = Guid.Parse("a1a2a3a4-a1a2-a1a2-a1a2-a1a2a3a4a5a6");
        var doctorUser = new User
        {
            Id = doctorUserId,
            ClinicId = clinicId,
            Name = "Dr. Henrique Lima",
            Email = "henrique.lima@clinicaaurora.com",
            PasswordHash = passwordHasher.Hash("ChangeMe123!"),
            Role = UserRole.Doctor,
            CreatedAt = now,
            UpdatedAt = now
        };

        var doctor = new Doctor
        {
            Id = doctorId,
            ClinicId = clinicId,
            Name = "Dr. Henrique Lima",
            Specialty = "Cardiologia",
            Crm = "CRM-SP-123456",
            Phone = "11998887766",
            Email = "henrique.lima@clinicaaurora.com",
            CreatedAt = now,
            UpdatedAt = now
        };

        var patient = new Patient
        {
            Id = patientId,
            ClinicId = clinicId,
            Name = "Ana Martins",
            Cpf = "12345678900",
            BirthDate = new DateOnly(1990, 3, 18),
            Phone = "11999998888",
            Email = "ana.martins@email.com",
            HealthInsurance = "SulAmerica",
            Notes = "Paciente demo do ambiente local.",
            PatientAccessToken = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            CreatedAt = now,
            UpdatedAt = now
        };

        var appointmentStart = new DateTimeOffset(2026, 5, 7, 12, 0, 0, TimeSpan.Zero);
        var appointment = new Appointment
        {
            Id = appointmentId,
            ClinicId = clinicId,
            PatientId = patientId,
            DoctorId = doctorId,
            StartAt = appointmentStart,
            EndAt = appointmentStart.AddMinutes(30),
            Status = AppointmentStatus.Scheduled,
            ConfirmationStatus = ConfirmationStatus.Pending,
            Source = AppointmentSource.Internal,
            Type = "Primeira consulta",
            Amount = 250m,
            Notes = "Criado automaticamente pelo seed local.",
            CreatedAt = now,
            UpdatedAt = now
        };

        var receivable = new Receivable
        {
            Id = receivableId,
            ClinicId = clinicId,
            AppointmentId = appointmentId,
            OriginalAmount = 250m,
            ReceivedAmount = 0m,
            Status = ReceivableStatus.Pending,
            DueDate = appointmentStart,
            Description = "Consulta inicial seed",
            CreatedAt = now,
            UpdatedAt = now
        };

        var expense1 = new Expense
        {
            Id = Guid.Parse("eeeeeeee-1111-1111-1111-eeeeeeeeeeee"),
            ClinicId = clinicId,
            Description = "Aluguel da sala - Maio/2026",
            Amount = 5000m,
            Category = ExpenseCategory.Rent,
            PaymentMethod = PaymentMethod.Pix,
            PaidAt = new DateTimeOffset(2026, 5, 5, 0, 0, 0, TimeSpan.Zero),
            Status = ExpenseStatus.Paid,
            CreatedAt = now,
            UpdatedAt = now
        };

        var expense2 = new Expense
        {
            Id = Guid.Parse("eeeeeeee-2222-2222-2222-eeeeeeeeeeee"),
            ClinicId = clinicId,
            Description = "Material de escritorio",
            Amount = 350m,
            Category = ExpenseCategory.Supplies,
            PaymentMethod = PaymentMethod.CreditCard,
            PaidAt = new DateTimeOffset(2026, 5, 3, 0, 0, 0, TimeSpan.Zero),
            Status = ExpenseStatus.Paid,
            CreatedAt = now,
            UpdatedAt = now
        };

        var expense3 = new Expense
        {
            Id = Guid.Parse("eeeeeeee-3333-3333-3333-eeeeeeeeeeee"),
            ClinicId = clinicId,
            Description = "Conta de energia",
            Amount = 890m,
            Category = ExpenseCategory.Utilities,
            PaymentMethod = PaymentMethod.DebitCard,
            PaidAt = new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero),
            Status = ExpenseStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

        var outbox = new OutboxEvent
        {
            ClinicId = clinicId,
            EventType = "appointment.created",
            PayloadJson = $$"""{"appointmentId":"{{appointmentId}}","patientId":"{{patientId}}","doctorId":"{{doctorId}}"}""",
            Status = OutboxStatus.Pending,
            Attempts = 0,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.AddRange(clinic, platformAdmin, clinicAdmin, doctorUser, doctor, patient, appointment, receivable, expense1, expense2, expense3, outbox);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seed completed. Demo clinic and users created.");
    }
}
