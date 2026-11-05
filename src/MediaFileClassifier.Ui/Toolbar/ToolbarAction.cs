using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Ui.Commands;

namespace MediaFileClassifier.Ui.Toolbar;

/// <summary>
/// An action button on the toolbar (E13-S03-T02). It runs an application command, so its label, icon,
/// enablement and tooltip with the shortcut are the ones the menus show (E13-S05).
/// </summary>
/// <param name="command">The command the button runs.</param>
/// <param name="order">Position among the actions, left to right in ascending order.</param>
/// <param name="priority">How long the button stays on the toolbar as the window narrows: the lowest priority
/// moves into the overflow menu first.</param>
public sealed partial class ToolbarAction(AppCommand command, int order, int priority) : ObservableObject
{
    /// <summary>
    /// Gets the command the button runs.
    /// </summary>
    public AppCommand Command { get; } = command;

    /// <summary>
    /// Gets the position among the actions.
    /// </summary>
    public int Order { get; } = order;

    /// <summary>
    /// Gets how long the button stays on the toolbar as the window narrows.
    /// </summary>
    public int Priority { get; } = priority;

    /// <summary>
    /// Gets or sets a value indicating whether the action doesn't fit on the toolbar and is in the overflow menu
    /// instead. <see cref="ToolbarActionPanel"/> sets it.
    /// </summary>
    [ObservableProperty]
    public partial bool IsOverflowed { get; set; }
}
