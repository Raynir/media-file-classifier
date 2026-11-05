using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Toolbar;

namespace MediaFileClassifier.Ui.Libraries;

/// <summary>
/// Puts the library actions on the toolbar (E13-S03-T02): Add folder (E02-S03). Rescan (E02-S04) comes here too.
/// </summary>
/// <param name="commands">The command registry.</param>
public sealed class LibraryToolbar(CommandRegistry commands) : IToolbarContributor
{
    /// <summary>
    /// The position of Add folder among the toolbar actions.
    /// </summary>
    public const int AddFolderOrder = 100;

    /// <summary>
    /// How long Add folder stays on the toolbar as the window narrows.
    /// </summary>
    public const int AddFolderPriority = 100;

    /// <inheritdoc />
    public void Register(ToolbarViewModel toolbar) => toolbar.AddAction(commands[LibraryCommands.AddFolder], AddFolderOrder, AddFolderPriority);
}
