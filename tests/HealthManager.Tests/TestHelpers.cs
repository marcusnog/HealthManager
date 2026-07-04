using HealthManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HealthManager.Tests;

internal static class TestHelpers
{
    internal static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }
}
