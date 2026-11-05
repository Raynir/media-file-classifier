using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Models;
using MediaFileClassifier.UseCases.Models.DownloadModel;
using MediaFileClassifier.UseCases.Models.GetModelOffer;
using MediaFileClassifier.UseCases.Models.ImportModel;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Ui.ViewModels.Models;

/// <summary>
/// What the "no model" banner shows.
/// </summary>
public enum ModelBannerState
{
    /// <summary>The embedding model is installed, or the app hasn't checked yet: no banner.</summary>
    Hidden,

    /// <summary>No embedding model: Download and Import from file.</summary>
    Missing,

    /// <summary>Downloading, with progress and Cancel.</summary>
    Downloading,

    /// <summary>Checking the files and moving them into the model store.</summary>
    Installing,

    /// <summary>The download or import failed: the error and Retry.</summary>
    Failed,
}

/// <summary>
/// The persistent "no model" banner in the main window (E06-S12-T04): search needs the embedding model, so while it's
/// missing the banner offers to download or import it, and it hides once the model is installed (D032, D037).
/// </summary>
public sealed partial class ModelBannerViewModel : ViewModelBase, IDisposable
{
    private readonly ISender sender;
    private readonly IModelAvailability? availability;
    private readonly IModelDialogs dialogs;
    private readonly Localizer localizer;
    private readonly ILogger logger;
    private CancellationTokenSource? download;
    private Func<Task>? lastAction;
    private ModelOffer? offer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelBannerViewModel"/> class.
    /// </summary>
    /// <param name="sender">Sends use case requests.</param>
    /// <param name="dialogs">The consent and file dialogs.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="availability">
    /// Which models are installed, or <see langword="null"/> where the app runs without AI (then the banner never shows).
    /// </param>
    /// <param name="logger">Logger.</param>
    public ModelBannerViewModel(
        ISender sender,
        IModelDialogs dialogs,
        Localizer localizer,
        IModelAvailability? availability = null,
        ILogger<ModelBannerViewModel>? logger = null)
    {
        this.sender = sender;
        this.availability = availability;
        this.dialogs = dialogs;
        this.localizer = localizer;
        this.logger = logger ?? (ILogger)NullLogger.Instance;
        if (availability is not null)
        {
            availability.Changed += OnModelsChanged;
            Dispatcher.UIThread.Post(() => _ = RefreshAsync());
        }
    }

    /// <summary>
    /// Gets what the banner shows.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible), nameof(IsMissing), nameof(IsDownloading), nameof(IsInstalling), nameof(IsFailed), nameof(Message))]
    public partial ModelBannerState State { get; private set; }

    /// <summary>
    /// Gets the download progress, from 0 to 100.
    /// </summary>
    [ObservableProperty]
    public partial double ProgressPercent { get; private set; }

    /// <summary>
    /// Gets the download progress as text, e.g. "120 MB of 1.4 GB".
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Message))]
    public partial string ProgressText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets what went wrong, for the user.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Message))]
    public partial string Error { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the technical detail of the error, shown as a tooltip.
    /// </summary>
    [ObservableProperty]
    public partial string ErrorDetail { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the banner shows.
    /// </summary>
    public bool IsVisible => State != ModelBannerState.Hidden;

    /// <summary>
    /// Gets a value indicating whether Download and Import from file show.
    /// </summary>
    public bool IsMissing => State == ModelBannerState.Missing;

    /// <summary>
    /// Gets a value indicating whether the progress bar and Cancel show.
    /// </summary>
    public bool IsDownloading => State == ModelBannerState.Downloading;

    /// <summary>
    /// Gets a value indicating whether the indeterminate progress shows.
    /// </summary>
    public bool IsInstalling => State == ModelBannerState.Installing;

    /// <summary>
    /// Gets a value indicating whether Retry shows.
    /// </summary>
    public bool IsFailed => State == ModelBannerState.Failed;

    /// <summary>
    /// Gets the banner's text.
    /// </summary>
    public string Message => State switch
    {
        ModelBannerState.Downloading => localizer.Format("ModelBanner_Downloading", ProgressText),
        ModelBannerState.Installing => localizer.Get("ModelBanner_Installing"),
        ModelBannerState.Failed => Error,
        _ => localizer.Get("ModelBanner_Message"),
    };

    /// <summary>
    /// Checks whether the embedding model is installed, and shows the banner if it isn't.
    /// </summary>
    /// <returns>A task that completes when the banner is up to date.</returns>
    public async Task RefreshAsync()
    {
        try
        {
            offer = await sender.Send(new GetModelOfferQuery(ModelPurpose.Embedding));
        }
        catch (Exception ex)
        {
            // Runs fire-and-forget on the UI thread: a failure leaves the banner as it is, and is logged.
            LogRefreshFailed(ex);
            return;
        }

        if (State is ModelBannerState.Downloading or ModelBannerState.Installing)
        {
            return;
        }

        State = offer is null or { IsInstalled: true } ? ModelBannerState.Hidden : State == ModelBannerState.Failed ? State : ModelBannerState.Missing;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (availability is not null)
        {
            availability.Changed -= OnModelsChanged;
        }

        download?.Cancel();
        download?.Dispose();
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (offer is null || !await dialogs.ConfirmDownloadAsync(offer))
        {
            return;
        }

        lastAction = RunDownloadAsync;
        await RunDownloadAsync();
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var paths = await dialogs.PickModelFilesAsync();
        if (paths.Count == 0)
        {
            return;
        }

        lastAction = () => RunImportAsync(paths);
        await RunImportAsync(paths);
    }

    [RelayCommand]
    private Task RetryAsync() => lastAction?.Invoke() ?? Task.CompletedTask;

    [RelayCommand]
    private void CancelDownload() => download?.Cancel();

    private async Task RunDownloadAsync()
    {
        using var cancellation = new CancellationTokenSource();
        download = cancellation;
        SetProgress(new ModelDownloadProgress(ModelDownloadPhase.Downloading, 0, offer?.Size ?? 0));
        State = ModelBannerState.Downloading;
        try
        {
            await sender.Send(new DownloadModelCommand(ModelPurpose.Embedding, new Progress<ModelDownloadProgress>(SetProgress)), cancellation.Token);
            State = ModelBannerState.Hidden;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            State = ModelBannerState.Missing;
        }
        catch (ModelDownloadException ex)
        {
            Fail(ex.Failure switch
            {
                ModelDownloadFailure.DiskFull => "ModelBanner_ErrorDiskFull",
                ModelDownloadFailure.HashMismatch => "ModelBanner_ErrorHashMismatch",
                ModelDownloadFailure.Install => "ModelBanner_ErrorInstall",
                _ => "ModelBanner_ErrorNetwork",
            }, ex);
        }
        finally
        {
            download = null;
        }

        await RefreshAsync();
    }

    private async Task RunImportAsync(IReadOnlyList<string> paths)
    {
        State = ModelBannerState.Installing;
        try
        {
            await sender.Send(new ImportModelCommand(ModelPurpose.Embedding, paths, null));
            State = ModelBannerState.Hidden;
        }
        catch (Exception ex) when (ex is ModelStoreException or IOException or UnauthorizedAccessException)
        {
            Fail("ModelBanner_ErrorImport", ex);
        }

        await RefreshAsync();
    }

    private void Fail(string key, Exception exception)
    {
        LogFailed(exception);
        Error = localizer.Get(key);
        ErrorDetail = exception.Message;
        State = ModelBannerState.Failed;
    }

    private void SetProgress(ModelDownloadProgress progress)
    {
        if (progress.Phase == ModelDownloadPhase.Installing)
        {
            State = ModelBannerState.Installing;
            return;
        }

        ProgressPercent = progress.Fraction * 100;
        ProgressText = localizer.Format("ModelBanner_Progress", Format.FileSize(progress.BytesDone), Format.FileSize(progress.TotalBytes));
    }

    private void OnModelsChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() => _ = RefreshAsync());

    [LoggerMessage(Level = LogLevel.Warning, Message = "Getting the search model failed")]
    private partial void LogFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Checking for the search model failed")]
    private partial void LogRefreshFailed(Exception exception);
}
