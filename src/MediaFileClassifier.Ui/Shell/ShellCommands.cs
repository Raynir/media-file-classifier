using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Platform;
using MediaFileClassifier.Ui.Theming;

namespace MediaFileClassifier.Ui.Shell;

/// <summary>
/// The app shell's own commands (E13): panels and layout, theme, pseudo-localisation, the log folder and exit.
/// </summary>
public sealed class ShellCommands : ICommandContributor
{
    /// <summary>Command ID: show or hide the navigation panel.</summary>
    public const string ToggleNavigation = "view.toggle-navigation";

    /// <summary>Command ID: collapse or expand the navigation panel.</summary>
    public const string CollapseNavigation = "view.collapse-navigation";

    /// <summary>Command ID: show the navigation panel expanded.</summary>
    public const string NavigationExpanded = "view.navigation-expanded";

    /// <summary>Command ID: show the navigation panel collapsed to its icon rail.</summary>
    public const string NavigationCollapsed = "view.navigation-collapsed";

    /// <summary>Command ID: hide the navigation panel.</summary>
    public const string NavigationHidden = "view.navigation-hidden";

    /// <summary>Command ID: show or hide the details panel.</summary>
    public const string ToggleDetails = "view.toggle-details";

    /// <summary>Command ID: collapse or expand the details panel.</summary>
    public const string CollapseDetails = "view.collapse-details";

    /// <summary>Command ID: show the details panel expanded.</summary>
    public const string DetailsExpanded = "view.details-expanded";

    /// <summary>Command ID: show the details panel collapsed to a strip.</summary>
    public const string DetailsCollapsed = "view.details-collapsed";

    /// <summary>Command ID: hide the details panel.</summary>
    public const string DetailsHidden = "view.details-hidden";

    /// <summary>Command ID: show or hide the status bar.</summary>
    public const string ToggleStatusBar = "view.toggle-status-bar";

    /// <summary>Command ID: restore the default panel layout.</summary>
    public const string ResetLayout = "view.reset-layout";

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

        AppCommand[] layoutCommands =
        [
            .. AddPanelCommands(registry, layout.Navigation, 100, "Navigation", "Icon.PanelLeftOpen", "Icon.Menu"),
            .. AddPanelCommands(registry, layout.Details, 110, "Details", "Icon.PanelRightOpen", "Icon.PanelRightClose"),
            registry.Add(new AppCommand(ToggleStatusBar, CommandGroups.View, 140, "Command_ToggleStatusBar", () => layout.IsStatusBarVisible = !layout.IsStatusBarVisible)
            {
                ToggleType = MenuItemToggleType.CheckBox,
                IsCheckedRule = () => layout.IsStatusBarVisible,
            }),
        ];
        registry.Add(new AppCommand(ResetLayout, CommandGroups.View, 150, "Command_ResetLayout", layout.Reset));
        layout.StateChanged += (_, _) =>
        {
            foreach (var command in layoutCommands)
            {
                command.Refresh();
            }
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

    // Each side panel gets: show or hide, collapse or expand, and a choice of Expanded, Collapsed and Hidden,
    // all in the panel's View submenu. Their shortcuts are in DefaultKeymap. IDs and keys follow the panel's name,
    // e.g. view.toggle-navigation, Command_ToggleNavigation and Menu_NavigationPanel.
    private static AppCommand[] AddPanelCommands(
        CommandRegistry registry, SidePanel panel, int order, string name, string toggleIcon, string collapseIcon)
    {
        var id = name.ToLowerInvariant();
        var submenu = $"Menu_{name}Panel";

        AppCommand Add(string commandId, int offset, string labelKey, Action execute, Func<bool> isChecked)
            => registry.Add(new AppCommand(commandId, CommandGroups.View, order + offset, labelKey, execute)
            {
                Submenu = submenu,
                ToggleType = MenuItemToggleType.Radio,
                IsCheckedRule = isChecked,
            });

        return
        [
            Add($"view.{id}-expanded", 0, "Command_PanelExpanded", panel.Expand, () => panel.IsExpanded),
            Add($"view.{id}-collapsed", 1, "Command_PanelCollapsed", panel.Collapse, () => panel.IsRail),
            Add($"view.{id}-hidden", 2, "Command_PanelHidden", panel.Hide, () => !panel.IsShown),
            registry.Add(new AppCommand($"view.toggle-{id}", CommandGroups.View, order + 5, $"Command_Toggle{name}", panel.ToggleVisible)
            {
                Submenu = submenu,
                IconKey = toggleIcon,
            }),
            registry.Add(new AppCommand($"view.collapse-{id}", CommandGroups.View, order + 6, $"Command_Collapse{name}", panel.ToggleCollapsed)
            {
                Submenu = submenu,
                IconKey = collapseIcon,
            }),
        ];
    }

    private AppCommand AddThemeCommand(CommandRegistry registry, string id, int order, string labelKey, string iconKey, ThemeMode mode)
        => registry.Add(new AppCommand(id, CommandGroups.View, order, labelKey, () => theme.Mode = mode)
        {
            IconKey = iconKey,
            ToggleType = MenuItemToggleType.Radio,
            IsCheckedRule = () => theme.Mode == mode,
        });
}
