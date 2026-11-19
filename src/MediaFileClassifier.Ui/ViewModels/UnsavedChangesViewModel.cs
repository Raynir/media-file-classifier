using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Ui.Editing;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.ViewModels;

/// <summary>
/// The prompt shown when the user leaves staged changes (E07-S12-T01): Save, Discard or Stay.
/// </summary>
public sealed partial class UnsavedChangesViewModel : ViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnsavedChangesViewModel"/> class.
    /// </summary>
    /// <param name="description">What the changes are about, e.g. an item's title.</param>
    /// <param name="localizer">UI strings.</param>
    public UnsavedChangesViewModel(string description, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        Title = localizer["UnsavedChanges_Title"];
        Message = localizer.Format("UnsavedChanges_Message", description);
    }

    /// <summary>
    /// Raised when the user chose.
    /// </summary>
    public event EventHandler<UnsavedChangesChoice>? Answered;

    /// <summary>
    /// Gets the window's title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the message, naming what has unsaved changes.
    /// </summary>
    public string Message { get; }

    [RelayCommand]
    private void Save() => Answered?.Invoke(this, UnsavedChangesChoice.Save);

    [RelayCommand]
    private void Discard() => Answered?.Invoke(this, UnsavedChangesChoice.Discard);

    [RelayCommand]
    private void Stay() => Answered?.Invoke(this, UnsavedChangesChoice.Stay);
}
