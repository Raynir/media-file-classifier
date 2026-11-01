using System.Text.Json;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Infrastructure.Paths;
using MediaFileClassifier.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Settings;

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
            Values = SettingValues.Empty
                .With("ai.openAiCompatibleEndpoint", JsonSerializer.SerializeToElement("http://localhost:1234/v1"))
                .With("library.retainedPreviousRuns", JsonSerializer.SerializeToElement(3)),
        };

        CreateService().Save(settings);
        CreateService().Save(settings);
        var loaded = CreateService().Current;

        Assert.Equal(settings, loaded);
        Assert.False(File.Exists(appPaths.SettingsFile + ".tmp"));
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsTheMainWindowLayout()
    {
        var layout = new MainWindowLayout(new PanelLayout(true, true, 300), new PanelLayout(false, false, 420));
        MonitorPlacement[] placements =
        [
            new("0,0,2560x1440@1.5", new WindowPlacement(10, 20, 1200, 700, IsMaximized: true)),
            new("0,0,1920x1080@1", new WindowPlacement(0, 0, 1280, 800, IsMaximized: false)),
        ];

        CreateService().Save(new AppSettings { MainWindowLayout = layout, MainWindowPlacements = placements });
        var loaded = CreateService().Current;

        Assert.Equal(layout, loaded.MainWindowLayout);
        Assert.Equal(placements, loaded.MainWindowPlacements);
    }

    [Fact]
    public void Current_Version1File_MovesTheEndpointIntoValues()
    {
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(appPaths.SettingsFile, """
            {
              "schemaVersion": 1,
              "lastLibraryPath": "D:\\Media",
              "providers": { "openAiCompatibleEndpoint": "http://localhost:11434" }
            }
            """);

        var settings = CreateService().Current;

        Assert.Equal(@"D:\Media", settings.LastLibraryPath);
        Assert.Equal("http://localhost:11434", settings.Values[KnownSettings.OpenAiCompatibleEndpoint.Key].GetString());
    }

    [Fact]
    public void Save_WritesTheCurrentSchemaVersionWithoutProviders()
    {
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(appPaths.SettingsFile, """{ "schemaVersion": 1, "providers": { "openAiCompatibleEndpoint": "" } }""");
        var service = CreateService();

        service.Save(service.Current);

        var json = File.ReadAllText(appPaths.SettingsFile);
        Assert.Contains("\"schemaVersion\": 2", json, StringComparison.Ordinal);
        Assert.DoesNotContain("providers", json, StringComparison.Ordinal);
        Assert.Empty(CreateService().Current.Values);
    }

    [Fact]
    public void Update_ChangesAndStoresTheCurrentSettings()
    {
        var service = CreateService();
        service.Save(new AppSettings { LastLibraryPath = "a" });

        var updated = service.Update(settings => settings with { Values = settings.Values.With("x", JsonSerializer.SerializeToElement(true)) });

        Assert.Equal("a", updated.LastLibraryPath);
        Assert.Same(updated, service.Current);
        Assert.True(CreateService().Current.Values["x"].GetBoolean());
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
