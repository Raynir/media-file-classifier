using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.Shortcuts;
using MediaFileClassifier.Ui.StatusBar;

namespace MediaFileClassifier.Ui.ViewModels;

/// <summary>
/// View model of the main window (E13-S01). Navigation is view-model-first: features fill the areas through
/// <see cref="Regions"/>, and the window shows the views that <see cref="ViewLocator"/> maps to them.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindowViewModel"/> class.
    /// </summary>
    /// <param name="layout">Which panels are shown, and how wide.</param>
    /// <param name="regions">What the areas show.</param>
    /// <param name="commands">The command registry, for binding buttons and menus by command ID.</param>
    /// <param name="menu">The main menu.</param>
    /// <param name="statusBar">The status bar.</param>
    /// <param name="modelBanner">The "no model" banner.</param>
    /// <param name="toasts">The toasts in the corner of the window.</param>
    /// <param name="shortcuts">The shortcut reference. Taking it also builds the keymap, which gives the
    /// commands their shortcuts.</param>
    public MainWindowViewModel(
        ShellLayout layout,
        ShellRegions regions,
        CommandRegistry commands,
        MainMenuViewModel menu,
        StatusBarViewModel statusBar,
        ShortcutReferenceViewModel shortcuts,
        Models.ModelBannerViewModel modelBanner,
        Notifications.ToastHostViewModel toasts)
    {
        Toasts = toasts;
        ModelBanner = modelBanner;
        Layout = layout;
        Regions = regions;
        Commands = commands;
        Menu = menu;
        StatusBar = statusBar;
        Shortcuts = shortcuts;
        NavigationContextMenu =
        [
            commands[ShellCommands.CollapseNavigation],
            commands[ShellCommands.ToggleNavigation],
            new MenuSeparator(),
            commands[ShellCommands.ResetLayout],
        ];
        DetailsContextMenu =
        [
            commands[ShellCommands.CollapseDetails],
            commands[ShellCommands.ToggleDetails],
            new MenuSeparator(),
            commands[ShellCommands.ResetLayout],
        ];
        StatusBarContextMenu = [commands[ShellCommands.ToggleStatusBar]];
    }

    /// <summary>
    /// Gets the "no model" banner (E06-S12-T04).
    /// </summary>
    public Models.ModelBannerViewModel ModelBanner { get; }

    /// <summary>
    /// Gets which panels are shown, and how wide.
    /// </summary>
    public ShellLayout Layout { get; }

    /// <summary>
    /// Gets what the navigation, toolbar, media browser and details areas show.
    /// </summary>
    public ShellRegions Regions { get; }

    /// <summary>
    /// Gets the command registry. Bind a surface with <c>{Binding Commands[view.toggle-navigation]}</c>.
    /// </summary>
    public CommandRegistry Commands { get; }

    /// <summary>
    /// Gets the main menu.
    /// </summary>
    public MainMenuViewModel Menu { get; }

    /// <summary>
    /// Gets the status bar.
    /// </summary>
    public StatusBarViewModel StatusBar { get; }

    /// <summary>
    /// Gets the toasts (E15-S01).
    /// </summary>
    public Notifications.ToastHostViewModel Toasts { get; }

    /// <summary>
    /// Gets the shortcut reference overlay.
    /// </summary>
    public ShortcutReferenceViewModel Shortcuts { get; }

    /// <summary>
    /// Gets the entries of the navigation panel's context menu.
    /// </summary>
    public IReadOnlyList<IMenuEntry> NavigationContextMenu { get; }

    /// <summary>
    /// Gets the entries of the details panel's context menu.
    /// </summary>
    public IReadOnlyList<IMenuEntry> DetailsContextMenu { get; }

    /// <summary>
    /// Gets the entries of the status bar's context menu.
    /// </summary>
    public IReadOnlyList<IMenuEntry> StatusBarContextMenu { get; }
}
