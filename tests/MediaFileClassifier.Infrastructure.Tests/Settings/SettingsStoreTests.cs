using System.Text.Json;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Infrastructure.Paths;
using MediaFileClassifier.Infrastructure.Settings;
using MediaFileClassifier.Testing.Files;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Settings;

/// <summary>
/// Tests for <see cref="SettingsStore"/> (E17-S01-T01).
/// </summary>
public sealed class SettingsStoreTests : IDisposable
{
    private static readonly ChoiceSetting<DayOfWeek> Day = new("test.day", SettingScope.App, SettingCategories.General, "Setting_Day", DayOfWeek.Monday);

    private readonly TemporaryFolder temp = new();
    private readonly string dataDirectory;
    private readonly AppPaths appPaths;
    private readonly FakeLibraryValues library = new();

    public SettingsStoreTests()
    {
        dataDirectory = temp.Combine("data");
        appPaths = new AppPaths(dataDirectory);
    }

    [Fact]
    public void Get_NothingStored_ReturnsTheDefault()
    {
        var store = CreateStore();

        Assert.Equal(DayOfWeek.Monday, store.Get(Day));
        Assert.Equal(string.Empty, store.Get(KnownSettings.OpenAiCompatibleEndpoint));
    }

    [Fact]
    public void SetValue_IsReadBackTypedAfterARestart()
    {
        CreateStore().SetValue(Day, DayOfWeek.Friday);

        Assert.Equal(DayOfWeek.Friday, CreateStore().Get(Day));
        Assert.Contains("\"test.day\": \"friday\"", File.ReadAllText(appPaths.SettingsFile), StringComparison.Ordinal);
    }

    [Fact]
    public void SetValue_KeepsOtherAppSettings()
    {
        var service = CreateService();
        service.Save(new AppSettings { LastLibraryPath = @"D:\Library" });

        new SettingsStore(service, library, NullLogger<SettingsStore>.Instance).SetValue(Day, DayOfWeek.Sunday);

        Assert.Equal(@"D:\Library", CreateService().Current.LastLibraryPath);
    }

    [Fact]
    public void SetValue_RaisesChangedOnlyWhenTheValueChanges()
    {
        var store = CreateStore();
        var changes = new List<SettingDefinition>();
        store.Changed += (_, e) => changes.Add(e.Setting);

        store.SetValue(Day, DayOfWeek.Monday);
        store.SetValue(Day, DayOfWeek.Tuesday);
        store.SetValue(Day, DayOfWeek.Tuesday);

        Assert.Equal([Day], changes);
    }

    [Fact]
    public void SetValue_InvalidValue_Throws()
    {
        var store = CreateStore();

        Assert.Throws<ArgumentException>(() => store.SetValue(KnownSettings.OpenAiCompatibleEndpoint, "not a url"));
        Assert.Throws<ArgumentException>(() => store.SetValue(Day, 3));
    }

    [Fact]
    public void Get_InvalidStoredValue_ReturnsTheDefault()
    {
        CreateService().Save(new AppSettings
        {
            Values = SettingValues.Empty.With(Day.Key, JsonSerializer.SerializeToElement("someday")),
        });

        Assert.Equal(DayOfWeek.Monday, CreateStore().Get(Day));
    }

    [Fact]
    public void LibrarySettings_WithoutALibrary_ReadDefaultsAndRejectWrites()
    {
        var store = CreateStore();

        Assert.False(store.IsAvailable(SettingScope.Library));
        Assert.Equal(1, store.Get(KnownSettings.RetainedPreviousRuns));
        Assert.Throws<InvalidOperationException>(() => store.SetValue(KnownSettings.RetainedPreviousRuns, 2));
    }

    [Fact]
    public void LibrarySettings_WithALibrary_AreStoredInTheLibrary()
    {
        var store = CreateStore();
        var availabilityChanges = 0;
        store.AvailabilityChanged += (_, _) => availabilityChanges++;

        library.Open();
        store.SetValue(KnownSettings.RetainedPreviousRuns, 4);

        Assert.Equal(1, availabilityChanges);
        Assert.Equal(4, store.Get(KnownSettings.RetainedPreviousRuns));
        Assert.Equal(4, library.Values[KnownSettings.RetainedPreviousRuns.Key].GetInt32());
        Assert.False(CreateService().Current.Values.ContainsKey(KnownSettings.RetainedPreviousRuns.Key));
    }

    [Fact]
    public void Reset_RemovesStoredValuesAndRaisesChangedForChangedOnes()
    {
        var store = CreateStore();
        library.Open();
        store.SetValue(Day, DayOfWeek.Friday);
        store.SetValue(KnownSettings.OpenAiCompatibleEndpoint, "http://localhost:1234");
        store.SetValue(KnownSettings.RetainedPreviousRuns, 3);
        var changes = new List<SettingDefinition>();
        store.Changed += (_, e) => changes.Add(e.Setting);

        store.Reset([Day, KnownSettings.RetainedPreviousRuns, KnownSettings.AnalyzeAutomatically]);

        Assert.Equal([Day, KnownSettings.RetainedPreviousRuns], changes);
        Assert.Equal(DayOfWeek.Monday, store.Get(Day));
        Assert.Equal(1, store.Get(KnownSettings.RetainedPreviousRuns));
        Assert.Equal("http://localhost:1234", store.Get(KnownSettings.OpenAiCompatibleEndpoint));
        Assert.False(CreateService().Current.Values.ContainsKey(Day.Key));
        Assert.Empty(library.Values);
    }

    [Fact]
    public void Reset_SkipsLibrarySettingsWithoutALibrary()
    {
        var store = CreateStore();
        store.SetValue(Day, DayOfWeek.Friday);

        store.Reset([Day, KnownSettings.RetainedPreviousRuns]);

        Assert.Equal(DayOfWeek.Monday, store.Get(Day));
    }

    public void Dispose()
    {
        temp.Dispose();
    }

    private JsonSettingsService CreateService() => new(appPaths, NullLogger<JsonSettingsService>.Instance);

    private SettingsStore CreateStore() => new(CreateService(), library, NullLogger<SettingsStore>.Instance);

    private sealed class FakeLibraryValues : ILibrarySettingValues
    {
        public event EventHandler? AvailabilityChanged;

        public bool IsAvailable { get; private set; }

        public Dictionary<string, JsonElement> Values { get; } = [];

        public void Open()
        {
            IsAvailable = true;
            AvailabilityChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool TryGet(string key, out JsonElement value) => Values.TryGetValue(key, out value);

        public void Apply(IReadOnlyDictionary<string, JsonElement?> changes)
        {
            foreach (var (key, value) in changes)
            {
                if (value is { } json)
                {
                    Values[key] = json;
                }
                else
                {
                    Values.Remove(key);
                }
            }
        }
    }
}
