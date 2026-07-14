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
            new FakeStorageService());

        var response = await service.CreateAsync(
            new CreatePatientRequest("Maria", "935.411.347-80", new DateOnly(1990, 1, 1), "11999999999", "maria@example.com", "Unimed", null, null),
            CancellationToken.None);

        response.Cpf.Should().Be("93541134780");
        dbContext.Patients.Should().ContainSingle(x => x.Id == response.Id);
    }

    private static AppDbContext CreateDbContext() => TestHelpers.CreateDbContext();
}
