using FluentAssertions;
using HealthManager.Application;
using HealthManager.Domain;
using HealthManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests;

public sealed class PatientServiceTests
{
    [Fact]
    public async Task CreateAsync_ShouldNormalizeCpfAndPersistPatient()
    {
        var clinicId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        dbContext.Clinics.Add(new Clinic { Id = clinicId, Name = "Clinica A", Slug = "clinica-a" });
        await dbContext.SaveChangesAsync();

        var service = new PatientService(
            dbContext,
            new FakeTenantProvider(clinicId),
            new FakeStorageService(),
            new CreatePatientRequestValidator(),
            new UpdatePatientRequestValidator(),
            new CreatePatientDocumentRequestValidator());

        var response = await service.CreateAsync(
            new CreatePatientRequest("Maria", "123.456.789-00", new DateOnly(1990, 1, 1), "11999999999", "maria@example.com", "Unimed", null),
            CancellationToken.None);

        response.Cpf.Should().Be("12345678900");
        dbContext.Patients.Should().ContainSingle(x => x.Id == response.Id);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }
}

