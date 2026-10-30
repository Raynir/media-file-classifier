using Avalonia;
using Avalonia.Styling;
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
        var service = new ThemeService();

        service.Mode = ThemeMode.Dark;
        var dark = Application.Current!.RequestedThemeVariant;
        service.Mode = ThemeMode.Light;
        var light = Application.Current.RequestedThemeVariant;
        service.Mode = ThemeMode.System;

        Assert.Equal(ThemeVariant.Dark, dark);
        Assert.Equal(ThemeVariant.Light, light);
        Assert.Equal(ThemeVariant.Default, Application.Current.RequestedThemeVariant);
    });
}
