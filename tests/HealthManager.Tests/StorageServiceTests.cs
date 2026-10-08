using FluentAssertions;
using HealthManager.Application;
using HealthManager.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;

namespace HealthManager.Tests;

public sealed class StorageServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task LocalStorage_ResolvesWithoutS3_AndRoundTripsDocument(string? bucket)
    {
        var storageRoot = Path.Combine(Path.GetTempPath(), "healthmanager-storage-tests", Guid.NewGuid().ToString());
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AWS_S3_BUCKET"] = bucket,
            ["LOCAL_STORAGE_ROOT"] = storageRoot,
            ["USE_INMEMORY_DATABASE"] = "true"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration, new TestEnvironment());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        try
        {
            var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();
            var path = storage.BuildPatientDocumentPath(Guid.NewGuid(), Guid.NewGuid(), "Documento.pdf");
            var bytes = new byte[] { 1, 2, 3, 4 };
            using var content = new MemoryStream(bytes);
            await storage.UploadPatientDocumentAsync(path, content, "application/pdf", CancellationToken.None);
            using var downloaded = await storage.DownloadPatientDocumentAsync(path, CancellationToken.None);
            using var result = new MemoryStream();
            await downloaded.CopyToAsync(result);
            result.ToArray().Should().Equal(bytes);
        }
        finally
        {
            if (Directory.Exists(storageRoot))
                Directory.Delete(storageRoot, recursive: true);
        }
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "HealthManager.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
