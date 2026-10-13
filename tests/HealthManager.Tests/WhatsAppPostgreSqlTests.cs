using FluentAssertions;
using HealthManager.Application;
using HealthManager.Domain;
using HealthManager.Infrastructure;
using HealthManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace HealthManager.Tests;

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HEALTHMANAGER_TEST_POSTGRES")))
            Skip = "Set HEALTHMANAGER_TEST_POSTGRES to an isolated PostgreSQL server with database creation permission.";
    }
}

public sealed class WhatsAppPostgreSqlTests
{
    [PostgreSqlFact]
    public async Task Migrations_PaginationAndUniqueKeys_WorkOnPostgreSql()
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("HEALTHMANAGER_TEST_POSTGRES"))
        {
            Database = "hm_atendimento_test_" + Guid.NewGuid().ToString("N"), Pooling = false
        };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection.ConnectionString).Options;
        await using var db = new AppDbContext(options);
        try
        {
            var migrations = db.Database.GetMigrations().ToArray();
            await db.GetService<IMigrator>().MigrateAsync(migrations[^2]);
            var clinic = Guid.NewGuid();
            db.Clinics.Add(new Clinic { Id = clinic, Name = "Teste", Slug = clinic.ToString() });
            await db.SaveChangesAsync();
            for (var i = 0; i < 2; i++)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "WhatsAppMessages" ("Id", "ClinicId", "Phone", "Message", "Status", "Direction", "ProviderMessageId", "CreatedAt", "UpdatedAt")
                    VALUES ({Guid.NewGuid()}, {clinic}, '(55) 99999-8888', 'legacy', 3, 1, 'legacy-duplicate', now(), now());
                    """);
            await db.Database.MigrateAsync();
            var legacy = await db.WhatsAppMessages.Where(x => x.Message == "legacy").ToListAsync();
            legacy.Should().HaveCount(2).And.OnlyContain(x => x.Phone == "5555999998888");
            legacy.Count(x => x.ProviderMessageId == "legacy-duplicate").Should().Be(1);
            db.Patients.Add(new Patient { ClinicId = clinic, Name = "Ana", Cpf = "", Phone = "11999998888" });
            db.WhatsAppMessages.AddRange(
                new WhatsAppMessage { ClinicId = clinic, Phone = "5511999998888", Message = "antiga", Direction = MessageDirection.Inbound, ProviderTimestamp = DateTimeOffset.UtcNow, ProviderMessageId = "inbound" },
                new WhatsAppMessage { ClinicId = clinic, Phone = "5511888887777", Message = "outra" });
            await db.SaveChangesAsync();
            db.WhatsAppMessages.Add(new WhatsAppMessage { ClinicId = clinic, Phone = "5511999998888", Message = "recente" });
            await db.SaveChangesAsync();
            var service = new WhatsAppAtendimentoService(db, new FakeTenantProvider(clinic), new WhatsAppMessagingService(db, new FakeMetaCloudApiClient(), new OutboxService(db)));
            var list = await service.ListAsync(new(1, 1, "ana"), default);
            list.Total.Should().Be(1);
            list.Items.Single().LastMessage.Should().Be("recente");
            list.Items.Single().LastInboundAt.Should().NotBeNull();
            var history = await service.MessagesAsync("11999998888", default, 1, 1);
            history.Total.Should().Be(2);
            history.Items.Single().Message.Should().Be("recente");

            db.WhatsAppMessages.Add(new WhatsAppMessage { ClinicId = clinic, Phone = "5511999998888", Direction = MessageDirection.Inbound, ProviderMessageId = "inbound" });
            await FluentActions.Invoking(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        }
        finally
        {
            // Delete only the randomly named database created by this test.
            await db.Database.EnsureDeletedAsync();
        }
    }
}
