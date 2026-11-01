using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;

namespace MediaFileClassifier.Ui.Theming;

/// <summary>
/// Switches between the light and dark themes (E13-S06-T01). The app follows the system theme
/// unless <see cref="Mode"/> overrides it. The choice is the <see cref="ThemeSetting"/> setting (E17-S05).
/// </summary>
public sealed partial class ThemeService : ObservableObject
{
    /// <summary>
    /// The theme setting.
    /// </summary>
    public static readonly ChoiceSetting<ThemeMode> ThemeSetting = new(
        "appearance.theme", SettingScope.App, SettingCategories.Appearance, "Setting_Theme", ThemeMode.System)
    {
        HelpKey = "Setting_Theme_Help",
    };

    private readonly ISettingsStore settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThemeService"/> class.
    /// </summary>
    /// <param name="settings">The setting values, where the mode is kept.</param>
    public ThemeService(ISettingsStore settings)
    {
        this.settings = settings;
        Mode = settings.Get(ThemeSetting);
        settings.Changed += (_, e) =>
        {
            if (e.Setting == ThemeSetting)
            {
                Mode = settings.Get(ThemeSetting);
            }
        };
    }

    /// <summary>
    /// Gets or sets the theme mode. Setting it applies the theme at once and stores the choice.
    /// </summary>
    [ObservableProperty]
    public partial ThemeMode Mode { get; set; }

    /// <summary>
    /// Gets the Avalonia theme variant for a mode.
    /// </summary>
    /// <param name="mode">Theme mode.</param>
    /// <returns><see cref="ThemeVariant.Default"/> to follow the system, or a fixed variant.</returns>
    public static ThemeVariant ToVariant(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => ThemeVariant.Light,
        ThemeMode.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    /// <summary>
    /// Applies <see cref="Mode"/> to the running application, if there is one.
    /// </summary>
    public void Apply()
    {
        if (Application.Current is { } application)
        {
            application.RequestedThemeVariant = ToVariant(Mode);
        }
    }

    partial void OnModeChanged(ThemeMode value)
    {
        Apply();

        // The store ignores an unchanged value, so this doesn't loop with the Changed handler.
        settings.SetValue(ThemeSetting, value);
    }
}
