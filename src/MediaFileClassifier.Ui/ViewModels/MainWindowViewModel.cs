using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Shell;

namespace MediaFileClassifier.Ui.ViewModels;

/// <summary>
/// View model of the main window. Navigation is view-model-first: setting
/// <see cref="CurrentPage"/> shows the view that <see cref="ViewLocator"/> maps to it.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindowViewModel"/> class.
    /// </summary>
    /// <param name="layout">Which panels are shown.</param>
    /// <param name="commands">The command registry, for binding buttons and menus by command ID.</param>
    /// <param name="menu">The main menu.</param>
    public MainWindowViewModel(ShellLayout layout, CommandRegistry commands, MainMenuViewModel menu)
    {
        Layout = layout;
        Commands = commands;
        Menu = menu;
        NavigationContextMenu = [commands[ShellCommands.ToggleNavigation], commands[ShellCommands.ToggleDetails]];
    }

    /// <summary>
    /// Gets which panels are shown.
    /// </summary>
    public ShellLayout Layout { get; }

    /// <summary>
    /// Gets the command registry. Bind a surface with <c>{Binding Commands[view.toggle-navigation]}</c>.
    /// </summary>
    public CommandRegistry Commands { get; }

    /// <summary>
    /// Gets the main menu.
    /// </summary>
    public MainMenuViewModel Menu { get; }

    /// <summary>
    /// Gets the commands of the navigation panel's context menu.
    /// </summary>
    public IReadOnlyList<AppCommand> NavigationContextMenu { get; }

    /// <summary>
    /// The page shown in the main content area, or <c>null</c> for an empty window.
    /// </summary>
    [ObservableProperty]
    public partial ViewModelBase? CurrentPage { get; set; }
}
