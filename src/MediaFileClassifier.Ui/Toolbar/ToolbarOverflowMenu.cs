using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Ui.Commands;

namespace MediaFileClassifier.Ui.Toolbar;

/// <summary>
/// The toolbar's overflow menu (E13-S03-T03): the actions that don't fit, behind a More button at the end of the
/// row. The responsive layout (E13-S07) reuses it.
/// </summary>
public sealed partial class ToolbarOverflowMenu : ObservableObject
{
    /// <summary>
    /// Gets a value indicating whether any action is in the menu, so the More button shows.
    /// </summary>
    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    /// <summary>
    /// Gets the commands of the actions in the menu, left to right as they were on the toolbar.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<IMenuEntry> Entries { get; private set; } = [];

    /// <summary>
    /// Lists the actions that are in the menu now.
    /// </summary>
    /// <param name="actions">All the toolbar's actions, in order.</param>
    internal void Update(IEnumerable<ToolbarAction> actions)
    {
        IReadOnlyList<IMenuEntry> entries = [.. actions.Where(action => action.IsOverflowed).Select(action => action.Command)];
        if (!entries.SequenceEqual(Entries))
        {
            Entries = entries;
        }

        IsShown = entries.Count > 0;
    }
}
