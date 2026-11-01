using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.UseCases.Settings;
using MediaFileClassifier.UseCases.Settings.ResetSettings;
using MediaFileClassifier.UseCases.Settings.SetSettingValue;

namespace MediaFileClassifier.UseCases.Tests.Settings;

/// <summary>
/// Tests for <see cref="SetSettingValueCommandHandler"/> and <see cref="ResetSettingsCommandHandler"/>.
/// </summary>
public sealed class SettingsCommandTests
{
    private static readonly BooleanSetting General = new("general.flag", SettingScope.App, SettingCategories.General, "G", defaultValue: false);

    private readonly InMemorySettingsStore store = new();
    private readonly SettingRegistry registry = new([General, KnownSettings.OpenAiCompatibleEndpoint, KnownSettings.RetainedPreviousRuns]);

    [Fact]
    public async Task SetValue_ValidValue_IsStored()
    {
        var result = await SetAsync(KnownSettings.OpenAiCompatibleEndpoint, "http://localhost:1234");

        Assert.True(result.Succeeded);
        Assert.Equal("http://localhost:1234", store.Get(KnownSettings.OpenAiCompatibleEndpoint));
    }

    [Fact]
    public async Task SetValue_InvalidValue_ReturnsTheErrorAndKeepsTheOldValue()
    {
        var result = await SetAsync(KnownSettings.OpenAiCompatibleEndpoint, "localhost");

        Assert.Equal("SettingError_HttpUrl", result.Error?.MessageKey);
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public async Task SetValue_LibrarySettingWithoutALibrary_IsUnavailable()
    {
        var result = await SetAsync(KnownSettings.RetainedPreviousRuns, 2);

        Assert.Same(SetSettingValueResult.Unavailable, result);
    }

    [Fact]
    public async Task Reset_Category_ResetsOnlyThatCategory()
    {
        store.SetValue(General, true);
        store.SetValue(KnownSettings.OpenAiCompatibleEndpoint, "http://localhost:1234");

        await new ResetSettingsCommandHandler(registry, store).Handle(new ResetSettingsCommand(SettingCategories.AiModels), CancellationToken.None);

        Assert.True(store.Get(General));
        Assert.Equal(string.Empty, store.Get(KnownSettings.OpenAiCompatibleEndpoint));
    }

    [Fact]
    public async Task Reset_All_ResetsEveryAvailableSetting()
    {
        store.IsLibraryOpen = true;
        store.SetValue(General, true);
        store.SetValue(KnownSettings.RetainedPreviousRuns, 5);

        await new ResetSettingsCommandHandler(registry, store).Handle(new ResetSettingsCommand(Category: null), CancellationToken.None);

        Assert.False(store.Get(General));
        Assert.Equal(1, store.Get(KnownSettings.RetainedPreviousRuns));
    }

    private Task<SetSettingValueResult> SetAsync(SettingDefinition setting, object value)
        => new SetSettingValueCommandHandler(store).Handle(new SetSettingValueCommand(setting, value), CancellationToken.None);
}
