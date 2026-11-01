using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Platform;
using MediaFileClassifier.Ui.Theming;

namespace MediaFileClassifier.Ui.Shell;

/// <summary>
/// The app shell's own commands (E13): panels, theme, pseudo-localisation, the log folder and exit.
/// </summary>
public sealed class ShellCommands : ICommandContributor
{
    /// <summary>Command ID: expand or collapse the navigation panel.</summary>
    public const string ToggleNavigation = "view.toggle-navigation";

    /// <summary>Command ID: show or hide the details panel.</summary>
    public const string ToggleDetails = "view.toggle-details";

    /// <summary>Command ID: follow the system theme.</summary>
    public const string ThemeSystem = "view.theme-system";

    /// <summary>Command ID: light theme.</summary>
    public const string ThemeLight = "view.theme-light";

    /// <summary>Command ID: dark theme.</summary>
    public const string ThemeDark = "view.theme-dark";

    /// <summary>Command ID: switch pseudo-localisation on or off.</summary>
    public const string PseudoLocalization = "tools.pseudo-localization";

    /// <summary>Command ID: open the folder with the log files.</summary>
    public const string OpenLogFolder = "help.open-log-folder";

    /// <summary>Command ID: close the app.</summary>
    public const string Exit = "library.exit";

    private readonly ShellLayout layout;
    private readonly ThemeService theme;
    private readonly Localizer localizer;
    private readonly IAppPaths paths;
    private readonly IFolderLauncher folderLauncher;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellCommands"/> class.
    /// </summary>
    /// <param name="layout">Main window panels.</param>
    /// <param name="theme">Theme switching.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="paths">App folders.</param>
    /// <param name="folderLauncher">Opens folders in the file manager.</param>
    public ShellCommands(ShellLayout layout, ThemeService theme, Localizer localizer, IAppPaths paths, IFolderLauncher folderLauncher)
    {
        this.layout = layout;
        this.theme = theme;
        this.localizer = localizer;
        this.paths = paths;
        this.folderLauncher = folderLauncher;
    }

    /// <inheritdoc />
    public void Register(CommandRegistry registry)
    {
        registry.Add(new AppCommand(Exit, CommandGroups.Library, 900, "Command_Exit", ExitApp)
        {
            IconKey = "Icon.Exit",
            IsVisible = Application.Current?.ApplicationLifetime is IControlledApplicationLifetime,
        });

        var toggleNavigation = registry.Add(new AppCommand(
            ToggleNavigation, CommandGroups.View, 100, "Command_ToggleNavigation", () => layout.IsNavigationExpanded = !layout.IsNavigationExpanded)
        {
            IconKey = "Icon.Menu",
            Gesture = new KeyGesture(Key.D1, KeyModifiers.Control),
            ToggleType = MenuItemToggleType.CheckBox,
            IsCheckedRule = () => layout.IsNavigationExpanded,
        });
        var toggleDetails = registry.Add(new AppCommand(
            ToggleDetails, CommandGroups.View, 110, "Command_ToggleDetails", () => layout.IsDetailsPanelOpen = !layout.IsDetailsPanelOpen)
        {
            IconKey = "Icon.PanelRightOpen",
            Gesture = new KeyGesture(Key.D2, KeyModifiers.Control),
            ToggleType = MenuItemToggleType.CheckBox,
            IsCheckedRule = () => layout.IsDetailsPanelOpen,
        });
        layout.PropertyChanged += (_, _) =>
        {
            toggleNavigation.Refresh();
            toggleDetails.Refresh();
        };

        var themeCommands = new[]
        {
            AddThemeCommand(registry, ThemeSystem, 200, "Command_ThemeSystem", "Icon.ThemeSystem", ThemeMode.System),
            AddThemeCommand(registry, ThemeLight, 210, "Command_ThemeLight", "Icon.ThemeLight", ThemeMode.Light),
            AddThemeCommand(registry, ThemeDark, 220, "Command_ThemeDark", "Icon.ThemeDark", ThemeMode.Dark),
        };
        theme.PropertyChanged += (_, _) =>
        {
            foreach (var command in themeCommands)
            {
                command.Refresh();
            }
        };

        registry.Add(new AppCommand(
            PseudoLocalization, CommandGroups.Tools, 900, "Command_PseudoLocalization", () => localizer.IsPseudoLocalized = !localizer.IsPseudoLocalized)
        {
            IconKey = "Icon.Translate",
            ToggleType = MenuItemToggleType.CheckBox,
            IsCheckedRule = () => localizer.IsPseudoLocalized,
        });

        registry.Add(new AppCommand(OpenLogFolder, CommandGroups.Help, 100, "Command_OpenLogFolder", () => folderLauncher.OpenFolderAsync(paths.LogsDirectory))
        {
            IconKey = "Icon.FolderOpen",
            RequiredCapability = KnownCapabilities.RevealInFileExplorer.Id,
        });
    }

    private static void ExitApp()
    {
        // TryShutdown closes the windows the normal way, so the quit confirmation (D027) can still ask.
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.TryShutdown();
        }
        else if (Application.Current?.ApplicationLifetime is IControlledApplicationLifetime lifetime)
        {
            lifetime.Shutdown();
        }
    }

    private AppCommand AddThemeCommand(CommandRegistry registry, string id, int order, string labelKey, string iconKey, ThemeMode mode)
        => registry.Add(new AppCommand(id, CommandGroups.View, order, labelKey, () => theme.Mode = mode)
        {
            IconKey = iconKey,
            ToggleType = MenuItemToggleType.Radio,
            IsCheckedRule = () => theme.Mode == mode,
        });
}
