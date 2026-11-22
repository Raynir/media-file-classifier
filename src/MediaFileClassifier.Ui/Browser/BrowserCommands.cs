using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.ViewModels.Browser;

namespace MediaFileClassifier.Ui.Browser;

/// <summary>
/// The thumbnail grid's commands (E14-S01): thumbnail size (D046), showing the focused item, previous and next
/// (E14-S05), and Select all and Select none (E14-S03). Their keys are in the browser scope, so they work only while the grid has focus.
/// </summary>
public sealed class BrowserCommands : ICommandContributor
{
    /// <summary>Command ID: larger thumbnails.</summary>
    public const string LargerThumbnails = "view.thumbnails-larger";

    /// <summary>Command ID: smaller thumbnails.</summary>
    public const string SmallerThumbnails = "view.thumbnails-smaller";

    /// <summary>Command ID: show the results as the thumbnail grid (E14-S02-T03).</summary>
    public const string GridView = "view.grid";

    /// <summary>Command ID: show the results as the list (E14-S02-T03).</summary>
    public const string ListView = "view.list";

    /// <summary>Command ID: open the focused item.</summary>
    public const string OpenItem = "item.open";

    /// <summary>Command ID: show the previous result (E14-S05-T03).</summary>
    public const string PreviousItem = "item.previous";

    /// <summary>Command ID: show the next result (E14-S05-T03).</summary>
    public const string NextItem = "item.next";

    /// <summary>Command ID: select all results.</summary>
    public const string SelectAll = "edit.select-all";

    /// <summary>Command ID: deselect everything.</summary>
    public const string SelectNone = "edit.select-none";

    private readonly Func<ThumbnailGridViewModel> grid;
    private readonly SelectionModel selection;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserCommands"/> class.
    /// </summary>
    /// <param name="grid">Gets the thumbnail grid, once a command runs.</param>
    /// <param name="selection">The selection and the result count, which the enablement rules read: unlike the grid, resolving them needs no job services (MFC-1021).</param>
    public BrowserCommands(Func<ThumbnailGridViewModel> grid, SelectionModel selection)
    {
        this.grid = grid;
        this.selection = selection;
    }

    /// <inheritdoc />
    public void Register(CommandRegistry registry)
    {
        registry.Add(new AppCommand(LargerThumbnails, CommandGroups.View, 300, "Command_LargerThumbnails", () => grid().Larger()));
        registry.Add(new AppCommand(SmallerThumbnails, CommandGroups.View, 310, "Command_SmallerThumbnails", () => grid().Smaller()));
        registry.Add(new AppCommand(GridView, CommandGroups.View, 320, "Browser_GridView", () => grid().ShowGrid()));
        registry.Add(new AppCommand(ListView, CommandGroups.View, 330, "Browser_ListView", () => grid().ShowList()));
        registry.Add(new AppCommand(OpenItem, CommandGroups.Item, 10, "Command_OpenItem", () => grid().OpenAsync(null))
        {
            // Enter shows the item in the details panel (E14-S05); the context menu's Open is "Open in default app".
            InContextMenu = false,
            AppliesTo = HasResults,
        });

        // Previous and next follow the result order, as the selection moves (E14-S05-T03).
        registry.Add(new AppCommand(PreviousItem, CommandGroups.Item, 100, "Command_PreviousItem", () => grid().MoveAsync(GridMove.Left, extend: false, keepSelection: false))
        {
            InContextMenu = false,
            AppliesTo = HasResults,
        });
        registry.Add(new AppCommand(NextItem, CommandGroups.Item, 110, "Command_NextItem", () => grid().MoveAsync(GridMove.Right, extend: false, keepSelection: false))
        {
            InContextMenu = false,
            AppliesTo = HasResults,
        });
        registry.Add(new AppCommand(SelectAll, CommandGroups.Edit, 100, "Command_SelectAll", () => grid().SelectAllAsync()) { AppliesTo = HasResults });
        registry.Add(new AppCommand(SelectNone, CommandGroups.Edit, 110, "Command_SelectNone", () => grid().ClearSelection()) { AppliesTo = () => selection.Count > 0 });
    }

    private bool HasResults() => selection.ResultCount > 0;
}
