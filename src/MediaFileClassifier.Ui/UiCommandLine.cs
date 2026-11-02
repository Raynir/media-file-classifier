using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui;

/// <summary>
/// Command-line switches for checking the UI: <c>--pseudo-loc</c> lengthens every string (E13-S08-T03),
/// and <c>--theme=light</c> or <c>--theme=dark</c> overrides the theme for this session, without storing it (E13-S06-T01).
/// </summary>
public static class UiCommandLine
{
    /// <summary>
    /// Applies the switches found in the arguments.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <param name="services">Services built by the composition root.</param>
    public static void Apply(string[] args, IServiceProvider services)
    {
        Localizer.Current.IsPseudoLocalized = args.Contains("--pseudo-loc");

        const string ThemePrefix = "--theme=";
        var theme = args.LastOrDefault(arg => arg.StartsWith(ThemePrefix, StringComparison.OrdinalIgnoreCase));
        if (theme is not null && Enum.TryParse<ThemeMode>(theme[ThemePrefix.Length..], ignoreCase: true, out var mode))
        {
            services.GetRequiredService<ThemeService>().Override(mode);
        }
    }
}
