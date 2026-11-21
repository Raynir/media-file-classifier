using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.UseCases.Libraries.OpenLibrary;
using MediaFileClassifier.UseCases.Search.Filters;
using MediaFileClassifier.UseCases.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.UseCases.Tests.Settings;

/// <summary>
/// Tests for <see cref="SettingRegistry"/> and <see cref="SettingsRegistration"/> (E17-S01-T01).
/// </summary>
public sealed class SettingRegistryTests
{
    private static readonly BooleanSetting FeatureFlag = new("feature.flag", SettingScope.App, SettingCategories.General, "Setting_Flag", defaultValue: false);

    [Fact]
    public void AFeature_RegistersASettingAndReadsItTyped()
    {
        var store = new InMemorySettingsStore();
        using var provider = new ServiceCollection()
            .AddUseCases()
            .AddSetting(FeatureFlag)
            .AddSingleton<ISettingsStore>(store)
            .BuildServiceProvider();

        var registry = provider.GetRequiredService<SettingRegistry>();
        store.SetValue(FeatureFlag, true);

        Assert.Same(FeatureFlag, registry.Find("feature.flag"));
        Assert.True(provider.GetRequiredService<ISettingsStore>().Get(FeatureFlag));
    }

    [Fact]
    public void AddUseCases_RegistersTheKnownSettings()
    {
        using var provider = new ServiceCollection().AddUseCases().BuildServiceProvider();

        var registry = provider.GetRequiredService<SettingRegistry>();

        Assert.Contains(KnownSettings.OpenAiCompatibleEndpoint, registry.All);
        Assert.Contains(KnownSettings.AnalyzeAutomatically, registry.All);
        Assert.Contains(KnownSettings.RetainedPreviousRuns, registry.All);
    }

    [Fact]
    public void AddUseCases_RegistersTheGeneralSettings_OnByDefaultExceptRestoreFilters()
    {
        using var provider = new ServiceCollection().AddUseCases().BuildServiceProvider();
        var registry = provider.GetRequiredService<SettingRegistry>();
        var store = new InMemorySettingsStore();

        Assert.Contains(LibraryStartupSettings.OpenLastLibrary, registry.All);
        Assert.Contains(FilterSettings.RestoreOnStart, registry.All);
        Assert.Contains(KnownSettings.InformationalNotifications, registry.All);
        Assert.All(registry.All.Where(setting => setting.Category == SettingCategories.General), setting => Assert.Equal(SettingScope.App, setting.Scope));
        Assert.True(store.Get(KnownSettings.InformationalNotifications));
        Assert.True(store.Get(LibraryStartupSettings.OpenLastLibrary));
        Assert.False(store.Get(FilterSettings.RestoreOnStart));
    }

    [Fact]
    public void All_IsInCategoryOrderThenBySettingOrder()
    {
        var late = new BooleanSetting("general.late", SettingScope.App, SettingCategories.General, "L", false) { Order = 10 };
        var early = new BooleanSetting("general.early", SettingScope.App, SettingCategories.General, "E", false) { Order = 1 };
        var processing = new BooleanSetting("processing.a", SettingScope.App, SettingCategories.Processing, "P", false);

        var registry = new SettingRegistry([processing, KnownSettings.RetainedPreviousRuns, late, KnownSettings.AnalyzeAutomatically, early]);

        Assert.Equal([early, late, KnownSettings.AnalyzeAutomatically, KnownSettings.RetainedPreviousRuns, processing], registry.All);
        Assert.Equal([early, late], registry.InCategory(SettingCategories.General));
    }

    [Fact]
    public void Constructor_RejectsDuplicateKeys()
    {
        var copy = new BooleanSetting(FeatureFlag.Key, SettingScope.App, SettingCategories.General, "Other", false);

        Assert.Throws<InvalidOperationException>(() => new SettingRegistry([FeatureFlag, copy]));
    }

    [Fact]
    public void Constructor_AllowsTheSameSettingRegisteredTwice()
    {
        var registry = new SettingRegistry([FeatureFlag, FeatureFlag]);

        Assert.Single(registry.All);
    }

    [Fact]
    public void Constructor_RejectsUnknownCategories()
    {
        var setting = new BooleanSetting("x.y", SettingScope.App, "Shortcuts", "X", false);

        Assert.Throws<InvalidOperationException>(() => new SettingRegistry([setting]));
    }
}
