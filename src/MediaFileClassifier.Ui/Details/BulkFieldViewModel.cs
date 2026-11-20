using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Metadata.EditMetadata;
using MediaFileClassifier.UseCases.Metadata.GetBulkMetadata;

namespace MediaFileClassifier.Ui.Details;

/// <summary>
/// One field in the bulk editor (E07-S03-T01): the value every selected item shows, or "Multiple values" when they
/// differ. Nothing is written for a field the user doesn't touch; a typed value is set on every item, Clear sets an
/// empty one, and Revert gives the AI values back (all staged until Save, D019).
/// </summary>
public sealed partial class BulkFieldViewModel : ObservableObject
{
    private readonly Localizer localizer;
    private string loadedText = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="BulkFieldViewModel"/> class.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="labelKey">UI string key of the label.</param>
    /// <param name="localizer">UI strings.</param>
    public BulkFieldViewModel(DescriptiveField field, string labelKey, Localizer localizer)
    {
        Field = field;
        LabelKey = labelKey;
        this.localizer = localizer;
        Label = localizer.Get(labelKey);
    }

    /// <summary>
    /// Gets the field.
    /// </summary>
    public DescriptiveField Field { get; }

    /// <summary>
    /// Gets the UI string key of the label.
    /// </summary>
    public string LabelKey { get; }

    /// <summary>
    /// Gets or sets the label.
    /// </summary>
    [ObservableProperty]
    public partial string Label { get; set; }

    /// <summary>
    /// Gets or sets the text: the shared value, or empty while the items differ. Typing stages setting it on every item.
    /// </summary>
    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the selected items show different values.
    /// </summary>
    [ObservableProperty]
    public partial bool IsMixed { get; private set; }

    /// <summary>
    /// Gets the hint in the empty text box: "Multiple values" while the items differ.
    /// </summary>
    public string Placeholder => IsMixed ? localizer.Get("Bulk_MultipleValues") : string.Empty;

    /// <summary>
    /// Gets how many of the items have a value, e.g. "37 of 120 items have a value" while they differ.
    /// </summary>
    [ObservableProperty]
    public partial string? Counts { get; private set; }

    /// <summary>
    /// Gets a value indicating whether Clear is staged: every item gets an empty value.
    /// </summary>
    [ObservableProperty]
    public partial bool IsCleared { get; private set; }

    /// <summary>
    /// Gets a value indicating whether Revert is staged: every item loses its own value, so its AI value shows again.
    /// </summary>
    [ObservableProperty]
    public partial bool IsReverted { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the field has a staged change.
    /// </summary>
    public bool IsDirty => IsReverted || IsCleared || Text != loadedText;

    /// <summary>
    /// Gets a value indicating whether any selected item has a value of its own that Revert would remove.
    /// </summary>
    [ObservableProperty]
    public partial bool CanRevert { get; private set; }

    /// <summary>
    /// Gets a value indicating whether Clear is offered: it is the way to empty a field that shows "Multiple values".
    /// </summary>
    public bool CanClear => IsMixed && !IsCleared;

    /// <summary>
    /// Gets a value indicating whether a staged change can be taken back.
    /// </summary>
    public bool HasStagedAction => IsDirty;

    /// <summary>
    /// Gets the shown message about a value the service refused, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>
    /// Shows what the selection has for the field. Anything staged is dropped.
    /// </summary>
    /// <param name="view">The field's aggregate.</param>
    /// <param name="itemCount">The number of selected items.</param>
    public void Load(BulkFieldView view, int itemCount)
    {
        ArgumentNullException.ThrowIfNull(view);

        loadedText = view.Shared?.Text ?? string.Empty;
        IsMixed = view.IsMixed;
        CanRevert = view.UserValueCount > 0;
        Counts = view.IsMixed ? localizer.Format("Bulk_Field_Counts", Format.Number(view.ItemsWithValue), Format.Number(itemCount)) : null;
        Discard();
        OnPropertyChanged(nameof(Placeholder));
        OnPropertyChanged(nameof(CanClear));
    }

    /// <summary>
    /// Drops the staged change.
    /// </summary>
    public void Discard()
    {
        IsCleared = false;
        IsReverted = false;
        Error = null;
        Text = loadedText;
        Notify();
    }

    /// <summary>
    /// Gives the edit this field stages.
    /// </summary>
    /// <returns>The edit, or <see langword="null"/> when nothing is staged.</returns>
    internal MetadataEdit? ToEdit()
    {
        if (IsReverted)
        {
            return new MetadataEdit.RevertField(Field);
        }

        return IsDirty ? new MetadataEdit.SetField(Field, FieldValue.FromText(Text)) : null;
    }

    [RelayCommand]
    private void Clear()
    {
        IsReverted = false;
        Text = string.Empty;
        IsCleared = true;
        Notify();
    }

    [RelayCommand]
    private void Revert()
    {
        IsCleared = false;
        Text = loadedText;
        IsReverted = true;
        Notify();
    }

    [RelayCommand]
    private void Undo() => Discard();

    partial void OnTextChanged(string value)
    {
        if (IsCleared && value.Length > 0)
        {
            IsCleared = false;
        }

        if (IsReverted && value != loadedText)
        {
            IsReverted = false;
        }

        Notify();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(HasStagedAction));
        OnPropertyChanged(nameof(CanClear));
    }
}
