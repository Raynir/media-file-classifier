using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Processing.GetJobFailures;

namespace MediaFileClassifier.Ui.ViewModels.Processing;

/// <summary>
/// One failure in the details of a job in the processing panel (E05-S03-T03): the file, the stage that failed and what
/// went wrong, with Retry and Reveal file.
/// </summary>
public sealed partial class FailedItemViewModel
{
    private readonly ProcessingPanelViewModel panel;

    /// <summary>
    /// Initializes a new instance of the <see cref="FailedItemViewModel"/> class.
    /// </summary>
    /// <param name="failure">The failure.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="panel">The panel that runs the actions.</param>
    /// <param name="canReveal">Whether the platform can show a file in its file manager.</param>
    public FailedItemViewModel(FailedWorkItem failure, Localizer localizer, ProcessingPanelViewModel panel, bool canReveal)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(panel);

        Failure = failure;
        this.panel = panel;
        Title = failure.FileName ?? localizer.Get("Processing_WholeTarget");
        Path = failure.RelativePath;
        Stage = localizer.Find("Stage_" + failure.Stage) ?? failure.Stage;
        Error = string.IsNullOrWhiteSpace(failure.Error) ? localizer.Get("Processing_NoErrorMessage") : failure.Error;
        CanRetry = failure.ItemId is not null;
        CanReveal = canReveal && failure.ItemId is not null;
    }

    /// <summary>
    /// Gets the failure.
    /// </summary>
    public FailedWorkItem Failure { get; }

    /// <summary>
    /// Gets the file name, or a general name when the whole target failed.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the file's path in its folder, or <see langword="null"/>.
    /// </summary>
    public string? Path { get; }

    /// <summary>
    /// Gets the name of the stage that failed.
    /// </summary>
    public string Stage { get; }

    /// <summary>
    /// Gets what went wrong.
    /// </summary>
    public string Error { get; }

    /// <summary>
    /// Gets a value indicating whether the failure can be retried on its own: it has a media item.
    /// </summary>
    public bool CanRetry { get; }

    /// <summary>
    /// Gets a value indicating whether the file can be shown in the file manager.
    /// </summary>
    public bool CanReveal { get; }

    [RelayCommand]
    private Task RetryAsync() => panel.RetryItemAsync(this);

    [RelayCommand]
    private Task RevealAsync() => panel.RevealItemAsync(this);
}
