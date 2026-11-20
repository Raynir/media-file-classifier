using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.ViewModels.Details;
using MediaFileClassifier.Ui.Browser;

namespace MediaFileClassifier.Ui.Details;

/// <summary>
/// The metadata editor's commands (E07-S02-T04): Edit metadata (Item menu, context menu, <c>F2</c> or <c>E</c>) moves
/// the keyboard into the editor, and Save and Cancel (<c>Ctrl+S</c> and <c>Esc</c> in the editor) end an edit (D019).
/// </summary>
public sealed class MetadataEditorCommands : ICommandContributor
{
    /// <summary>Command ID: focus the metadata editor.</summary>
    public const string Edit = "item.edit-metadata";

    /// <summary>Command ID: save the staged metadata changes.</summary>
    public const string Save = "metadata.save";

    /// <summary>Command ID: drop the staged metadata changes.</summary>
    public const string Cancel = "metadata.cancel";

    /// <summary>Command ID: put back what the last bulk edit changed (E07-S03-T03).</summary>
    public const string UndoBulk = "edit.undo";

    /// <summary>Command ID: the <c>Ctrl+Z</c> shortcut for the bulk undo, which works only while several items are selected.</summary>
    public const string UndoBulkShortcut = "edit.undo-bulk";

    private readonly SelectionModel selection;
    private readonly Func<DetailsPanelViewModel> panel;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataEditorCommands"/> class.
    /// </summary>
    /// <param name="selection">The media browser's selection.</param>
    /// <param name="panel">Gets the details panel, which holds the editor.</param>
    public MetadataEditorCommands(SelectionModel selection, Func<DetailsPanelViewModel> panel)
    {
        this.selection = selection;
        this.panel = panel;
    }

    /// <inheritdoc />
    public void Register(CommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var edit = registry.Add(new AppCommand(Edit, CommandGroups.Item, 110, "Command_EditMetadata", () => panel().RequestEdit())
        {
            AppliesTo = () => selection.Count > 0,
        });

        registry.Add(new AppCommand(Save, CommandGroups.Edit, 100, "Command_SaveMetadata", () => panel().IsBulk ? panel().BulkEditor.RequestSaveAsync() : panel().Editor.SaveAsync())
        {
            InContextMenu = false,
            AppliesTo = () => panel().IsBulk
                ? panel().BulkEditor.IsDirty && !panel().BulkEditor.IsSaving
                : panel().Editor.IsDirty && !panel().Editor.IsSaving,
        });
        registry.Add(new AppCommand(Cancel, CommandGroups.Edit, 110, "Command_CancelMetadata", () => CancelStaged())
        {
            InContextMenu = false,
            AppliesTo = () => panel().IsBulk ? panel().BulkEditor.IsDirty && !panel().BulkEditor.IsSaving : panel().Editor.IsDirty,
        });
        registry.Add(new AppCommand(UndoBulk, CommandGroups.Edit, 10, "Command_UndoBulk", () => panel().BulkEditor.UndoAsync())
        {
            InContextMenu = false,
            AppliesTo = () => panel().BulkEditor.CanUndo && !panel().BulkEditor.IsSaving,
        });
        registry.Add(new AppCommand(UndoBulkShortcut, CommandGroups.Edit, 11, "Command_UndoBulk", () => panel().BulkEditor.UndoAsync())
        {
            InContextMenu = false,
            IsVisible = false,
            AppliesTo = () => panel().IsBulk && panel().BulkEditor.CanUndo && !panel().BulkEditor.IsSaving,
        });
        selection.Changed += (_, _) => edit.Refresh();
    }

    private Task CancelStaged()
    {
        if (panel().IsBulk)
        {
            panel().BulkEditor.Cancel();
        }
        else
        {
            panel().Editor.Cancel();
        }

        return Task.CompletedTask;
    }
}
