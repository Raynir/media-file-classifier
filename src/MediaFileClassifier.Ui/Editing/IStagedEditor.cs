namespace MediaFileClassifier.Ui.Editing;

/// <summary>
/// An editor that stages changes until the user saves them (D019), so the <see cref="UnsavedChangesGuard"/> can keep
/// them from being lost. The metadata editor is one; bulk editing (E07-S03) registers its own.
/// </summary>
public interface IStagedEditor
{
    /// <summary>
    /// Gets a value indicating whether changes are staged and not saved.
    /// </summary>
    bool HasUnsavedChanges { get; }

    /// <summary>
    /// Gets what the changes are about, for the prompt, e.g. an item's title.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Saves the staged changes.
    /// </summary>
    /// <returns><see langword="true"/> if nothing is staged any more; <see langword="false"/> if the save failed.</returns>
    Task<bool> SaveAsync();

    /// <summary>
    /// Drops the staged changes.
    /// </summary>
    void Discard();
}
