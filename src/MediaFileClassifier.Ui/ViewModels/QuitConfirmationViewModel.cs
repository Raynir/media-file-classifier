using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.ViewModels;

/// <summary>
/// The confirmation shown when the window closes while work is pending (D027, E05-S01-T07).
/// </summary>
public sealed partial class QuitConfirmationViewModel : ViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QuitConfirmationViewModel"/> class.
    /// </summary>
    /// <param name="pendingCount">Number of work items still to do.</param>
    /// <param name="localizer">UI strings.</param>
    public QuitConfirmationViewModel(int pendingCount, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        Message = pendingCount == 1
            ? localizer["QuitConfirmation_MessageOne"]
            : localizer.Format("QuitConfirmation_Message", Format.Number(pendingCount));
    }

    /// <summary>
    /// Raised when the user chose: <see langword="true"/> to quit, <see langword="false"/> to keep the app open.
    /// </summary>
    public event EventHandler<bool>? Answered;

    /// <summary>
    /// Gets the message, with the number of items still processing.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets or sets whether to quit without asking from now on.
    /// </summary>
    [ObservableProperty]
    public partial bool DontAskAgain { get; set; }

    [RelayCommand]
    private void Quit() => Answered?.Invoke(this, true);

    [RelayCommand]
    private void Cancel() => Answered?.Invoke(this, false);
}
