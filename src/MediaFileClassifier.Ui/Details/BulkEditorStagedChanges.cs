using MediaFileClassifier.Ui.Editing;
using MediaFileClassifier.Ui.ViewModels.Details;

namespace MediaFileClassifier.Ui.Details;

/// <summary>
/// Lets the <see cref="UnsavedChangesGuard"/> save or drop what the bulk editor has staged (E07-S03-T02).
/// </summary>
/// <param name="editor">The bulk editor.</param>
/// <param name="describe">Names the selection being edited, e.g. "3 items selected".</param>
internal sealed class BulkEditorStagedChanges(BulkEditorViewModel editor, Func<string> describe) : IStagedEditor
{
    /// <inheritdoc />
    public bool HasUnsavedChanges => editor.IsDirty;

    /// <inheritdoc />
    public string Description => describe();

    /// <inheritdoc />
    public async Task<bool> SaveAsync()
    {
        if (editor.IsSaving)
        {
            return false;
        }

        // The user chose Save in the guard's question, so there is no second confirmation.
        await editor.SaveAsync();

        // A refused value, a failed write or a stopped save leaves the changes staged or the editor reloaded with a message.
        return !editor.IsDirty && editor.SaveError is null;
    }

    /// <inheritdoc />
    public void Discard() => editor.Cancel();
}
