using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Models;
using MediaFileClassifier.UseCases.Models.DeleteModel;
using MediaFileClassifier.UseCases.Models.DownloadModel;
using MediaFileClassifier.UseCases.Models.GetModelOffer;
using MediaFileClassifier.UseCases.Models.GetModelUsage;
using MediaFileClassifier.UseCases.Models.GetModels;
using MediaFileClassifier.UseCases.Models.ImportModel;
using MediaFileClassifier.UseCases.Models.VerifyModel;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Ui.ViewModels.Models;

/// <summary>
/// What a model row is doing.
/// </summary>
public enum ModelRowState
{
    /// <summary>Not installed: Download and Import from file.</summary>
    NotInstalled,

    /// <summary>Downloading, with progress and Cancel.</summary>
    Downloading,

    /// <summary>Checking the files and moving them into the model store.</summary>
    Installing,

    /// <summary>Installed: Verify and Delete.</summary>
    Installed,

    /// <summary>Hashing the installed files again.</summary>
    Verifying,

    /// <summary>Asks before deleting the model, naming the libraries that use it.</summary>
    ConfirmingDelete,

    /// <summary>Deleting the files.</summary>
    Deleting,
}

/// <summary>
/// One tier of a model on the model management page (E17-S08-T01).
/// </summary>
public sealed partial class ModelRowViewModel : ViewModelBase, IDisposable
{
    private readonly ISender sender;
    private readonly IModelDialogs dialogs;
    private readonly Localizer localizer;
    private readonly ILogger logger;
    private CancellationTokenSource? download;
    private string statusKey = string.Empty;
    private object?[] statusArgs = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelRowViewModel"/> class.
    /// </summary>
    /// <param name="entry">The model tier.</param>
    /// <param name="sender">Sends use case requests.</param>
    /// <param name="dialogs">The consent and file dialogs.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="logger">Logger.</param>
    public ModelRowViewModel(ModelListEntry entry, ISender sender, IModelDialogs dialogs, Localizer localizer, ILogger logger)
    {
        Entry = entry;
        this.sender = sender;
        this.dialogs = dialogs;
        this.localizer = localizer;
        this.logger = logger;
        State = entry.IsInstalled ? ModelRowState.Installed : ModelRowState.NotInstalled;
        SetStatus(entry.IsInstalled ? "Models_StatusInstalled" : "Models_StatusNotInstalled", isError: false);
    }

    /// <summary>
    /// Gets the model tier as last listed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(Details), nameof(LicenseUrl), nameof(CanDownload))]
    public partial ModelListEntry Entry { get; private set; }

    /// <summary>
    /// Gets what the row is doing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsBusy),
        nameof(CanDownload),
        nameof(IsInstalledIdle),
        nameof(IsDownloading),
        nameof(IsInstalling),
        nameof(IsConfirmingDelete))]
    public partial ModelRowState State { get; private set; }

    /// <summary>
    /// Gets the status line: installed, not installed, verified, or what went wrong.
    /// </summary>
    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether <see cref="Status"/> is an error.
    /// </summary>
    [ObservableProperty]
    public partial bool IsError { get; private set; }

    /// <summary>
    /// Gets the technical detail of the last error, shown as a tooltip.
    /// </summary>
    [ObservableProperty]
    public partial string? ErrorDetail { get; private set; }

    /// <summary>
    /// Gets the download progress, from 0 to 100.
    /// </summary>
    [ObservableProperty]
    public partial double ProgressPercent { get; private set; }

    /// <summary>
    /// Gets the question shown before deleting a model that libraries use.
    /// </summary>
    [ObservableProperty]
    public partial string DeleteQuestion { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the row's title, e.g. "SigLIP 2 — Compact".
    /// </summary>
    public string Name => localizer.Format("Models_Name", Entry.DisplayName, Entry.TierDisplayName);

    /// <summary>
    /// Gets the row's details: precision, size, license and version.
    /// </summary>
    public string Details
    {
        get
        {
            var parts = new List<string>(4);
            if (Entry.Precision is { } precision)
            {
                parts.Add(precision);
            }

            parts.Add(Format.FileSize(Entry.Size));
            parts.Add(Entry.License ?? localizer.Get("Models_LicenseUnknown"));
            parts.Add(localizer.Format("Models_Version", Entry.Version));
            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// Gets where the license text is, shown as the details' tooltip.
    /// </summary>
    public string? LicenseUrl => Entry.LicenseUrl?.ToString();

    /// <summary>
    /// Gets a value indicating whether the row is working.
    /// </summary>
    public bool IsBusy => State is ModelRowState.Downloading or ModelRowState.Installing or ModelRowState.Verifying or ModelRowState.Deleting;

    /// <summary>
    /// Gets a value indicating whether Download and Import from file show.
    /// </summary>
    public bool CanDownload => State == ModelRowState.NotInstalled && Entry.IsInCatalogue;

    /// <summary>
    /// Gets a value indicating whether Verify and Delete show.
    /// </summary>
    public bool IsInstalledIdle => State == ModelRowState.Installed;

    /// <summary>
    /// Gets a value indicating whether the progress bar and Cancel show.
    /// </summary>
    public bool IsDownloading => State == ModelRowState.Downloading;

    /// <summary>
    /// Gets a value indicating whether the indeterminate progress shows.
    /// </summary>
    public bool IsInstalling => State is ModelRowState.Installing or ModelRowState.Verifying or ModelRowState.Deleting;

    /// <summary>
    /// Gets a value indicating whether Delete anyway and Cancel show.
    /// </summary>
    public bool IsConfirmingDelete => State == ModelRowState.ConfirmingDelete;

    /// <summary>
    /// Checks whether a listed entry is this row's tier.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns><see langword="true"/> if it has the same model, version and tier.</returns>
    public bool Matches(ModelListEntry entry) =>
        entry.ModelId == Entry.ModelId && entry.Version == Entry.Version && entry.TierId == Entry.TierId;

    /// <summary>
    /// Takes a newly listed entry of the same tier. A row that's working keeps its state; an idle one follows the store.
    /// </summary>
    /// <param name="entry">The entry.</param>
    public void Update(ModelListEntry entry)
    {
        var wasInstalled = Entry.IsInstalled;
        Entry = entry;
        if (IsBusy || wasInstalled == entry.IsInstalled)
        {
            return;
        }

        State = entry.IsInstalled ? ModelRowState.Installed : ModelRowState.NotInstalled;
        SetStatus(entry.IsInstalled ? "Models_StatusInstalled" : "Models_StatusNotInstalled", isError: false);
    }

    /// <summary>
    /// Updates the texts after a culture or pseudo-localisation switch.
    /// </summary>
    public void RefreshTexts()
    {
        Status = statusArgs.Length == 0 ? localizer.Get(statusKey) : localizer.Format(statusKey, statusArgs);
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Details));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        download?.Cancel();
        download?.Dispose();
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (Entry.Purpose is not { } purpose)
        {
            return;
        }

        var offer = new ModelOffer(Name, Entry.Size, Entry.License ?? string.Empty, Entry.LicenseUrl!, Entry.Source!, IsInstalled: false);
        if (!await dialogs.ConfirmDownloadAsync(offer))
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        download = cancellation;
        ProgressPercent = 0;
        State = ModelRowState.Downloading;
        SetStatus("Models_StatusDownloading", isError: false, Format.FileSize(0), Format.FileSize(Entry.Size));
        try
        {
            var command = new DownloadModelCommand(purpose, new Progress<ModelDownloadProgress>(OnProgress)) { TierId = Entry.TierId };
            await sender.Send(command, cancellation.Token);
            Installed();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            State = ModelRowState.NotInstalled;
            SetStatus("Models_StatusNotInstalled", isError: false);
        }
        catch (ModelDownloadException ex)
        {
            Fail(ModelRowState.NotInstalled, ex.Failure switch
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
    }

    [RelayCommand]
    private void CancelDownload() => download?.Cancel();

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (Entry.Purpose is not { } purpose)
        {
            return;
        }

        var paths = await dialogs.PickModelFilesAsync();
        if (paths.Count == 0)
        {
            return;
        }

        State = ModelRowState.Installing;
        SetStatus("Models_StatusInstalling", isError: false);
        try
        {
            var installed = await sender.Send(new ImportModelCommand(purpose, paths, null));
            if (installed is null || installed.TierId == Entry.TierId)
            {
                Installed();
            }
            else
            {
                // The files were another tier's: that row shows it, this one stays as it was.
                State = ModelRowState.NotInstalled;
                SetStatus("Models_StatusImportedOtherTier", isError: false, installed.TierId);
            }
        }
        catch (Exception ex) when (ex is ModelStoreException or IOException or UnauthorizedAccessException)
        {
            Fail(ModelRowState.NotInstalled, "ModelBanner_ErrorImport", ex);
        }
    }

    [RelayCommand]
    private async Task VerifyAsync()
    {
        State = ModelRowState.Verifying;
        SetStatus("Models_StatusVerifying", isError: false);
        try
        {
            var problems = await sender.Send(new VerifyModelCommand(Entry.ModelId, Entry.Version, Entry.TierId));
            State = ModelRowState.Installed;
            if (problems.Count == 0)
            {
                SetStatus("Models_StatusVerified", isError: false);
            }
            else
            {
                SetStatus("Models_StatusVerifyFailed", isError: true);
                ErrorDetail = string.Join(Environment.NewLine, problems);
            }
        }
        catch (Exception ex) when (ex is ModelStoreException or IOException or UnauthorizedAccessException)
        {
            Fail(ModelRowState.Installed, "Models_ErrorVerify", ex);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        IReadOnlyList<string> usedBy;
        try
        {
            usedBy = await sender.Send(new GetModelUsageQuery(Entry.ModelId, Entry.Version, Entry.TierId));
        }
        catch (Exception ex) when (ex is ModelStoreException or IOException or UnauthorizedAccessException)
        {
            Fail(ModelRowState.Installed, "Models_ErrorDelete", ex);
            return;
        }

        DeleteQuestion = usedBy.Count == 0
            ? localizer.Format("Models_DeleteQuestion", Name)
            : localizer.Format("Models_DeleteQuestionUsed", Name, string.Join(", ", usedBy.Select(Path.GetFileName)));
        State = ModelRowState.ConfirmingDelete;
    }

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        State = ModelRowState.Deleting;
        SetStatus("Models_StatusDeleting", isError: false);
        try
        {
            await sender.Send(new DeleteModelCommand(Entry.ModelId, Entry.Version, Entry.TierId, Confirmed: true));
            State = ModelRowState.NotInstalled;
            SetStatus("Models_StatusNotInstalled", isError: false);
        }
        catch (Exception ex) when (ex is ModelStoreException or IOException or UnauthorizedAccessException)
        {
            Fail(ModelRowState.Installed, "Models_ErrorDelete", ex);
        }
    }

    [RelayCommand]
    private void CancelDelete() => State = ModelRowState.Installed;

    private void Installed()
    {
        State = ModelRowState.Installed;
        SetStatus("Models_StatusInstalledNow", isError: false);
    }

    private void OnProgress(ModelDownloadProgress progress)
    {
        if (State != ModelRowState.Downloading && State != ModelRowState.Installing)
        {
            return;
        }

        if (progress.Phase == ModelDownloadPhase.Installing)
        {
            State = ModelRowState.Installing;
            SetStatus("Models_StatusInstalling", isError: false);
            return;
        }

        ProgressPercent = progress.Fraction * 100;
        SetStatus("Models_StatusDownloading", isError: false, Format.FileSize(progress.BytesDone), Format.FileSize(progress.TotalBytes));
    }

    private void Fail(ModelRowState state, string key, Exception exception)
    {
        LogFailed(exception, Entry.ModelId, Entry.TierId);
        State = state;
        SetStatus(key, isError: true);
        ErrorDetail = exception.Message;
    }

    private void SetStatus(string key, bool isError, params object?[] args)
    {
        statusKey = key;
        statusArgs = args;
        Status = args.Length == 0 ? localizer.Get(key) : localizer.Format(key, args);
        IsError = isError;
        if (!isError)
        {
            ErrorDetail = null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A model action failed for {Model} ({Tier})")]
    private partial void LogFailed(Exception exception, string model, string tier);
}
