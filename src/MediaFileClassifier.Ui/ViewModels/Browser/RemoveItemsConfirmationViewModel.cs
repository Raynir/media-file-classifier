using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.ViewModels.Browser;

/// <summary>
/// The confirmation before items are removed from the library (E02-S10-T03), shared by the context menu, the Delete
/// key and the details panel's action. "Don't ask again" turns the confirmation off; the settings window turns it on.
/// </summary>
public sealed partial class RemoveItemsConfirmationViewModel : ViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RemoveItemsConfirmationViewModel"/> class.
    /// </summary>
    /// <param name="count">Number of items to remove.</param>
    /// <param name="localizer">UI strings.</param>
    public RemoveItemsConfirmationViewModel(int count, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        Question = count == 1
            ? localizer["RemoveItems_QuestionOne"]
            : localizer.Format("RemoveItems_Question", Format.Number(count));
    }

    /// <summary>
    /// Raised when the user chose: <see langword="true"/> to remove, <see langword="false"/> to cancel.
    /// </summary>
    public event EventHandler<bool>? Answered;

    /// <summary>
    /// Gets the question, with the number of items.
    /// </summary>
    public string Question { get; }

    /// <summary>
    /// Gets or sets whether to remove without asking from now on.
    /// </summary>
    [ObservableProperty]
    public partial bool DontAskAgain { get; set; }

    [RelayCommand]
    private void Remove() => Answered?.Invoke(this, true);

    [RelayCommand]
    private void Cancel() => Answered?.Invoke(this, false);
}
