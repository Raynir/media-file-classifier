using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Rescan;
using MediaFileClassifier.Ui.Toolbar;

namespace MediaFileClassifier.Ui.Libraries;

/// <summary>
/// Puts the library actions on the toolbar (E13-S03-T02): Add folder (E02-S03) and Rescan (E02-S04).
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

    /// <summary>
    /// The position of Import files among the toolbar actions, after Add folder (E03-S05).
    /// </summary>
    public const int ImportFilesOrder = 105;

    /// <summary>
    /// How long Import files stays on the toolbar as the window narrows: after Add folder, before Rescan.
    /// </summary>
    public const int ImportFilesPriority = 95;

    /// <summary>
    /// The position of Rescan among the toolbar actions, after Add folder.
    /// </summary>
    public const int RescanOrder = 110;

    /// <summary>
    /// How long Rescan stays on the toolbar as the window narrows; it goes into the overflow menu before Add folder.
    /// </summary>
    public const int RescanPriority = 90;

    /// <inheritdoc />
    public void Register(ToolbarViewModel toolbar)
    {
        toolbar.AddAction(commands[LibraryCommands.AddFolder], AddFolderOrder, AddFolderPriority);
        toolbar.AddAction(commands[LibraryCommands.ImportFiles], ImportFilesOrder, ImportFilesPriority);
        toolbar.AddAction(commands[RescanCommands.Rescan], RescanOrder, RescanPriority);
    }
}
