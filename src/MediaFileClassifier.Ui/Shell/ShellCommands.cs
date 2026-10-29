using Avalonia.Controls;
using Avalonia.Input;
using MediaFileClassifier.Ui.Commands;

namespace MediaFileClassifier.Ui.Shell;

/// <summary>
/// The app shell's own commands (E13): the panel toggles.
/// </summary>
public sealed class ShellCommands : ICommandContributor
{
    /// <summary>Command ID: expand or collapse the navigation panel.</summary>
    public const string ToggleNavigation = "view.toggle-navigation";

    /// <summary>Command ID: show or hide the details panel.</summary>
    public const string ToggleDetails = "view.toggle-details";

    private readonly ShellLayout layout;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellCommands"/> class.
    /// </summary>
    /// <param name="layout">Main window panels.</param>
    public ShellCommands(ShellLayout layout)
    {
        this.layout = layout;
    }

    /// <inheritdoc />
    public void Register(CommandRegistry registry)
    {
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
    }
}
