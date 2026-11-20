using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    [NotifyCanExecuteChangedFor(nameof(RevertCommand))]
    public partial bool CanRevert { get; set; }

    /// <summary>
    /// Gets a value indicating whether the user chose to revert the field to the AI or file value.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm), nameof(HasStagedAction))]
    public partial bool IsReverted { get; private set; }

    /// <summary>
    /// Gets the mark of the stored value: the AI's, the user's edit, or an accepted AI value (E07-S11-T01).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAi), nameof(IsEditedValue), nameof(IsAccepted), nameof(CanConfirm))]
    public partial FieldProvenance Provenance { get; private set; }

    /// <summary>
    /// Gets the tooltip of the provenance mark: the model and the date.
    /// </summary>
    [ObservableProperty]
    public partial string ProvenanceTip { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the stored value is the AI's.
    /// </summary>
    public bool IsAi => Provenance == FieldProvenance.Ai && !IsConfirmed;

    /// <summary>
    /// Gets a value indicating whether the stored value is one the user wrote.
    /// </summary>
    public bool IsEditedValue => Provenance == FieldProvenance.Edited;

    /// <summary>
    /// Gets a value indicating whether the stored value is an AI value the user confirmed.
    /// </summary>
    public bool IsAccepted => Provenance == FieldProvenance.Accepted || IsConfirmed;

    /// <summary>
    /// Gets a value indicating whether the user chose to confirm the AI value as it is (D020); the save stores it as
    /// the user's accepted value.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAi), nameof(IsAccepted), nameof(CanConfirm), nameof(HasStagedAction))]
    public partial bool IsConfirmed { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the AI value can be confirmed: it is the effective value and the text wasn't
    /// touched.
    /// </summary>
    public bool CanConfirm => !IsReadOnly && Provenance == FieldProvenance.Ai && !IsConfirmed && !IsReverted
        && string.Equals(Text, originalText, StringComparison.Ordinal);

    /// <summary>
    /// Gets a value indicating whether a revert or confirm is staged, which Undo takes back.
    /// </summary>
    public bool HasStagedAction => IsReverted || IsConfirmed;

    /// <summary>
    /// Gets a value indicating whether the current model can't produce the field and no layer has a value, so the
    /// editor says so (E06-S04-T03).
    /// </summary>
    [ObservableProperty]
    public partial bool IsNotAvailable { get; private set; }

    /// <summary>
    /// Gets a value indicating whether a newer AI value than the user's is waiting (D020).
    /// </summary>
    [ObservableProperty]
    public partial bool HasSuggestion { get; private set; }

    /// <summary>
    /// Gets the text the AI now suggests for the user's value.
    /// </summary>
    [ObservableProperty]
    public partial string SuggestionText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the tooltip of the new-suggestion mark: the model and the date.
    /// </summary>
    [ObservableProperty]
    public partial string SuggestionTip { get; private set; } = string.Empty;

    /// <summary>
    /// Gets or sets where the tooltips get their text; the editor sets it.
    /// </summary>
    internal ProvenanceText? Provenances { get; set; }

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
        ShowProvenance(view);
        IsReverted = false;
        IsConfirmed = false;
        Error = null;
        Text = originalText;
        IsDirty = false;
    }

    /// <summary>
    /// Takes the stored values of a field that changed in the background, keeping what the user staged: the staged text
    /// stays, and whether it is a change is worked out against the new stored value (E07-S12-T02).
    /// </summary>
    /// <param name="view">The field's layers as they are stored now.</param>
    internal void Rebase(FieldMetadataView view)
    {
        originalText = view.Effective?.Value.Text ?? string.Empty;
        FallbackText = (view.LatestAi?.Value ?? view.Technical?.Value)?.Text ?? string.Empty;
        CanRevert = view.User is not null;
        ShowProvenance(view);
        if (!CanRevert)
        {
            // The user's value is gone, so there is nothing to revert.
            IsReverted = false;
        }

        if (Provenance != FieldProvenance.Ai)
        {
            // The AI value isn't the effective one any more, so there is nothing to confirm.
            IsConfirmed = false;
        }

        if (IsReverted)
        {
            // A staged revert goes to the AI or file value as it is now.
            Text = FallbackText;
        }

        IsDirty = IsReverted || IsConfirmed || !string.Equals(Text, originalText, StringComparison.Ordinal);
        OnPropertyChanged(nameof(CanConfirm));
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Gets the value the text stands for: a list field's entries are split at commas and line breaks.
    /// </summary>
    /// <returns>The value to save.</returns>
    internal FieldValue ToValue() => IsList
        ? FieldValue.FromList(Text.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        : FieldValue.FromText(Text);

    /// <summary>
    /// Goes back to the loaded text, dropping the staged change.
    /// </summary>
    internal void Discard()
    {
        IsReverted = false;
        IsConfirmed = false;
        Error = null;
        Text = originalText;
        IsDirty = false;
    }

    /// <summary>
    /// Stages going back to the AI or file value.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRevert))]
    public void Revert()
    {
        IsConfirmed = false;
        IsReverted = true;
        Error = null;
        Text = FallbackText;
        IsDirty = true;
    }

    private void ShowProvenance(FieldMetadataView view)
    {
        Provenance = ProvenanceText.Of(view);
        ProvenanceTip = Provenances?.Tip(view) ?? string.Empty;
        IsNotAvailable = Provenances?.IsNotAvailable(view) ?? false;
        HasSuggestion = view.Suggestion is not null;
        SuggestionText = view.Suggestion?.Value.Text ?? string.Empty;
        SuggestionTip = view.Suggestion is { } suggestion ? Provenances?.SuggestionTip(suggestion) ?? string.Empty : string.Empty;
    }

    /// <summary>
    /// Stages confirming the AI value as it is: the save stores it as the user's accepted value (D020).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConfirm))]
    public void Confirm()
    {
        IsConfirmed = true;
        Error = null;
        IsDirty = true;
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Takes back the staged revert or confirm, and any typing, so the field shows what is stored.
    /// </summary>
    [RelayCommand]
    public void Undo()
    {
        Discard();
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    partial void OnTextChanged(string value)
    {
        // A revert stays staged while the text is the value it falls back to; typing something else replaces it.
        if (IsReverted && !string.Equals(value, FallbackText, StringComparison.Ordinal))
        {
            IsReverted = false;
        }

        if (IsConfirmed && !string.Equals(value, originalText, StringComparison.Ordinal))
        {
            // Typing over a confirmed AI value makes it the user's own edit.
            IsConfirmed = false;
        }

        Error = null;
        IsDirty = IsReverted || IsConfirmed || !string.Equals(value, originalText, StringComparison.Ordinal);
        OnPropertyChanged(nameof(CanConfirm));
        ConfirmCommand.NotifyCanExecuteChanged();
    }
}
