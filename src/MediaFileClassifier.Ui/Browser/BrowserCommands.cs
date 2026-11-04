using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.ViewModels.Browser;

namespace MediaFileClassifier.Ui.Browser;

/// <summary>
/// The thumbnail grid's commands (E14-S01): thumbnail size (D046), opening the focused item, and Select all and
/// Select none (E14-S03). Their keys are in the browser scope, so they work only while the grid has focus.
/// </summary>
public sealed class BrowserCommands : ICommandContributor
{
    /// <summary>Command ID: larger thumbnails.</summary>
    public const string LargerThumbnails = "view.thumbnails-larger";

    /// <summary>Command ID: smaller thumbnails.</summary>
    public const string SmallerThumbnails = "view.thumbnails-smaller";

    /// <summary>Command ID: open the focused item.</summary>
    public const string OpenItem = "item.open";

    /// <summary>Command ID: select all results.</summary>
    public const string SelectAll = "edit.select-all";

    /// <summary>Command ID: deselect everything.</summary>
    public const string SelectNone = "edit.select-none";

    private readonly Func<ThumbnailGridViewModel> grid;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserCommands"/> class.
    /// </summary>
    /// <param name="grid">Gets the thumbnail grid, once a command runs.</param>
    public BrowserCommands(Func<ThumbnailGridViewModel> grid)
    {
        this.grid = grid;
    }

    /// <inheritdoc />
    public void Register(CommandRegistry registry)
    {
        registry.Add(new AppCommand(LargerThumbnails, CommandGroups.View, 300, "Command_LargerThumbnails", () => grid().Larger()));
        registry.Add(new AppCommand(SmallerThumbnails, CommandGroups.View, 310, "Command_SmallerThumbnails", () => grid().Smaller()));
        registry.Add(new AppCommand(OpenItem, CommandGroups.Item, 10, "Command_OpenItem", () => grid().OpenAsync(null))
        {
            // Enter's Open becomes the preview (E14-S05); the context menu's Open is "Open in default app".
            InContextMenu = false,
        });
        registry.Add(new AppCommand(SelectAll, CommandGroups.Edit, 100, "Command_SelectAll", () => grid().SelectAllAsync()));
        registry.Add(new AppCommand(SelectNone, CommandGroups.Edit, 110, "Command_SelectNone", () => grid().ClearSelection()));
    }
}
