namespace MediaFileClassifier.Infrastructure.Tests.Settings;

using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Infrastructure.Paths;
using MediaFileClassifier.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Tests for <see cref="JsonSettingsService"/>.
/// </summary>
public sealed class JsonSettingsServiceTests : IDisposable
{
    private readonly string dataDirectory = Path.Combine(Path.GetTempPath(), "mfc-tests", Guid.NewGuid().ToString("N"));
    private readonly AppPaths appPaths;

    public JsonSettingsServiceTests()
    {
        appPaths = new AppPaths(dataDirectory);
    }

    [Fact]
    public void Current_MissingFile_ReturnsDefaults()
    {
        var service = CreateService();

        Assert.Equal(new AppSettings(), service.Current);
    }

    [Fact]
    public void Current_CorruptFile_ReturnsDefaultsAndKeepsCorruptCopy()
    {
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(appPaths.SettingsFile, "{ not json");

        var service = CreateService();

        Assert.Equal(new AppSettings(), service.Current);
        Assert.Equal("{ not json", File.ReadAllText(appPaths.SettingsFile + ".corrupt"));
    }

    [Fact]
    public void Save_ThenLoadInNewService_RoundTripsValues()
    {
        var settings = new AppSettings
        {
            LastLibraryPath = @"D:\Media\Library",
            MainWindow = new WindowPlacement(100, 200, 1024, 768, IsMaximized: true),
            Providers = new ProviderSettings { OpenAiCompatibleEndpoint = "http://localhost:1234/v1" },
        };

        CreateService().Save(settings);
        CreateService().Save(settings);
        var loaded = CreateService().Current;

        Assert.Equal(settings, loaded);
        Assert.False(File.Exists(appPaths.SettingsFile + ".tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(dataDirectory))
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private JsonSettingsService CreateService() => new(appPaths, NullLogger<JsonSettingsService>.Instance);
}
