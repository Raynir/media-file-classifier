namespace MediaFileClassifier.Ui.Editing;

/// <summary>
/// What the user chose in the unsaved changes prompt.
/// </summary>
public enum UnsavedChangesChoice
{
    /// <summary>Stay where you are and keep the changes. Also what closing the prompt means.</summary>
    Stay = 0,

    /// <summary>Save the changes, then go on.</summary>
    Save,

    /// <summary>Drop the changes, then go on.</summary>
    Discard,
}
