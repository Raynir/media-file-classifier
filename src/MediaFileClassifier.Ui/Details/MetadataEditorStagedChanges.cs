using MediaFileClassifier.Ui.Editing;
using MediaFileClassifier.Ui.ViewModels.Details;

namespace MediaFileClassifier.Ui.Details;

/// <summary>
/// Lets the <see cref="UnsavedChangesGuard"/> save or drop what the metadata editor has staged (E07-S12-T01).
/// </summary>
/// <param name="editor">The editor.</param>
/// <param name="describe">Names the item being edited.</param>
internal sealed class MetadataEditorStagedChanges(MetadataEditorViewModel editor, Func<string> describe) : IStagedEditor
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

        await editor.SaveAsync();

        // A refused value or a failed write leaves the changes staged, with the reason shown in the editor.
        return !editor.IsDirty;
    }

    /// <inheritdoc />
    public void Discard() => editor.Cancel();
}
