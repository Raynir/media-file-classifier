using Avalonia;
using Avalonia.Styling;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Ui.Theming;

namespace MediaFileClassifier.Ui.Tests.Theming;

/// <summary>
/// Tests for <see cref="ThemeService"/> (E13-S06-T01).
/// </summary>
public sealed class ThemeServiceTests
{
    [Theory]
    [InlineData(ThemeMode.System, "Default")]
    [InlineData(ThemeMode.Light, "Light")]
    [InlineData(ThemeMode.Dark, "Dark")]
    public void ToVariant_MapsEachMode(ThemeMode mode, string variant)
    {
        Assert.Equal(variant, ThemeService.ToVariant(mode).Key);
    }

    [Fact]
    public Task Mode_OverridesAndRestoresTheSystemTheme() => TestApp.RunAsync(() =>
    {
        var service = new ThemeService(new InMemorySettingsStore());

        service.Mode = ThemeMode.Dark;
        var dark = Application.Current!.RequestedThemeVariant;
        service.Mode = ThemeMode.Light;
        var light = Application.Current.RequestedThemeVariant;
        service.Mode = ThemeMode.System;

        Assert.Equal(ThemeVariant.Dark, dark);
        Assert.Equal(ThemeVariant.Light, light);
        Assert.Equal(ThemeVariant.Default, Application.Current.RequestedThemeVariant);
    });

    [Fact]
    public Task Mode_IsStoredAndRestored() => TestApp.RunAsync(() =>
    {
        var store = new InMemorySettingsStore();

        new ThemeService(store).Mode = ThemeMode.Dark;
        var restored = new ThemeService(store);

        Assert.Equal(ThemeMode.Dark, store.Get(ThemeService.ThemeSetting));
        Assert.Equal(ThemeMode.Dark, restored.Mode);
        restored.Mode = ThemeMode.System;
    });

    [Fact]
    public Task Mode_FollowsTheSettingWhenItChangesElsewhere() => TestApp.RunAsync(() =>
    {
        var store = new InMemorySettingsStore();
        var service = new ThemeService(store);

        store.SetValue(ThemeService.ThemeSetting, ThemeMode.Light);
        var mode = service.Mode;
        store.Reset([ThemeService.ThemeSetting]);

        Assert.Equal(ThemeMode.Light, mode);
        Assert.Equal(ThemeMode.System, service.Mode);
    });

    [Fact]
    public Task Override_AppliesWithoutStoring() => TestApp.RunAsync(() =>
    {
        var store = new InMemorySettingsStore();
        var service = new ThemeService(store);

        service.Override(ThemeMode.Dark);
        var variant = Application.Current!.RequestedThemeVariant;
        service.Override(ThemeMode.System);

        Assert.Equal(ThemeVariant.Dark, variant);
        Assert.Equal(0, store.WriteCount);
    });
}
