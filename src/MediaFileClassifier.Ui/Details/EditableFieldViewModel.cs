using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.UseCases.Metadata;

namespace MediaFileClassifier.Ui.Details;

/// <summary>
/// One field in the metadata editor (E07-S02): its text as the user edits it, and what it was when loaded, so the field
/// knows whether it changed. A list field (objects, scenes, ...) is edited as one comma-separated text.
/// </summary>
public sealed partial class EditableFieldViewModel : ViewModelBase
{
    private string originalText = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="EditableFieldViewModel"/> class.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="labelKey">Resource key of the label.</param>
    /// <param name="label">The label.</param>
    /// <param name="isReadOnly">Whether the user can't edit the field (the transcript).</param>
    public EditableFieldViewModel(DescriptiveField field, string labelKey, string label, bool isReadOnly = false)
    {
        Field = field;
        LabelKey = labelKey;
        Label = label;
        IsReadOnly = isReadOnly;
    }

    /// <summary>
    /// Gets the field.
    /// </summary>
    public DescriptiveField Field { get; }

    /// <summary>
    /// Gets the resource key of the label.
    /// </summary>
    public string LabelKey { get; }

    /// <summary>
    /// Gets or sets the label.
    /// </summary>
    [ObservableProperty]
    public partial string Label { get; set; }

    /// <summary>
    /// Gets a value indicating whether the field holds a list of entries.
    /// </summary>
    public bool IsList => DescriptiveFields.IsList(Field);

    /// <summary>
    /// Gets a value indicating whether the user can't edit the field.
    /// </summary>
    public bool IsReadOnly { get; }

    /// <summary>
    /// Gets a value indicating whether the field is a single line (the title).
    /// </summary>
    public bool IsSingleLine => Field == DescriptiveField.Title;

    /// <summary>
    /// Gets or sets the text the user sees and edits.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasText))]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the field has text.
    /// </summary>
    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    /// <summary>
    /// Gets a value indicating whether the field has an unsaved change.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDirty { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether the field has a value of the user's, which can be reverted to the AI or
    /// file value.
    /// </summary>
    [ObservableProperty]
    public partial bool CanRevert { get; set; }

    /// <summary>
    /// Gets a value indicating whether the user chose to revert the field to the AI or file value.
    /// </summary>
    [ObservableProperty]
    public partial bool IsReverted { get; private set; }

    /// <summary>
    /// Gets or sets the message of the validation error the last save found in this field, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>
    /// Gets the text the field had when it was loaded.
    /// </summary>
    internal string OriginalText => originalText;

    /// <summary>
    /// Gets the text the field falls back to when reverted: the AI value, else the file's.
    /// </summary>
    internal string FallbackText { get; private set; } = string.Empty;

    /// <summary>
    /// Sets the field from a freshly loaded view, clearing every staged change.
    /// </summary>
    /// <param name="view">The field's layers.</param>
    internal void Load(FieldMetadataView view)
    {
        originalText = view.Effective?.Value.Text ?? string.Empty;
        FallbackText = (view.LatestAi?.Value ?? view.Technical?.Value)?.Text ?? string.Empty;
        CanRevert = view.User is not null;
        IsReverted = false;
        Error = null;
        Text = originalText;
        IsDirty = false;
    }

    /// <summary>
    /// Goes back to the loaded text, dropping the staged change.
    /// </summary>
    internal void Discard()
    {
        IsReverted = false;
        Error = null;
        Text = originalText;
        IsDirty = false;
    }

    /// <summary>
    /// Stages going back to the AI or file value.
    /// </summary>
    internal void Revert()
    {
        IsReverted = true;
        Error = null;
        Text = FallbackText;
        IsDirty = true;
    }

    partial void OnTextChanged(string value)
    {
        // A revert stays staged while the text is the value it falls back to; typing something else replaces it.
        if (IsReverted && !string.Equals(value, FallbackText, StringComparison.Ordinal))
        {
            IsReverted = false;
        }

        Error = null;
        IsDirty = IsReverted || !string.Equals(value, originalText, StringComparison.Ordinal);
    }
}
