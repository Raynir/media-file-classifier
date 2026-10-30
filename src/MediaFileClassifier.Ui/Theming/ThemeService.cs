using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaFileClassifier.Ui.Theming;

/// <summary>
/// Switches between the light and dark themes (E13-S06-T01). The app follows the system theme
/// unless <see cref="Mode"/> overrides it. Saving the choice comes with the settings screen (E17).
/// </summary>
public sealed partial class ThemeService : ObservableObject
{
    /// <summary>
    /// Gets or sets the theme mode. Setting it applies the theme at once.
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

    partial void OnModeChanged(ThemeMode value) => Apply();
}
