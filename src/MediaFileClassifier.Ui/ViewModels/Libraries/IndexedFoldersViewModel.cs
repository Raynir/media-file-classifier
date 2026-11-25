using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Ui.Analysis;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Rescan;
using MediaFileClassifier.UseCases.Analysis.AnalyzeScope;
using MediaFileClassifier.UseCases.Libraries;
using MediaFileClassifier.UseCases.Libraries.AddLibraryFolders;
using MediaFileClassifier.UseCases.Libraries.ChangeFolderRules;
using MediaFileClassifier.UseCases.Libraries.CheckFolderAvailability;
using MediaFileClassifier.UseCases.Libraries.CheckFolderOverlap;
using MediaFileClassifier.UseCases.Libraries.GetIndexedFolders;
using MediaFileClassifier.UseCases.Libraries.IndexWholeFolder;
using MediaFileClassifier.UseCases.Libraries.RemoveFolder;
using MediaFileClassifier.UseCases.Libraries.UseThisDrive;
using MediaFileClassifier.UseCases.Monitoring;
using MediaFileClassifier.UseCases.Monitoring.ChangeMonitoringMode;
using MediaFileClassifier.UseCases.Monitoring.GetFolderMonitoring;
using MediatR;

namespace MediaFileClassifier.Ui.ViewModels.Libraries;

/// <summary>
/// The indexed folders dialog (E02-S03-T06): the open library's folders with their state and item counts, and adding,
/// removing and the exclusion rules. Merging folders and removing one are confirmed in the dialog first.
/// </summary>
public sealed partial class IndexedFoldersViewModel : ViewModelBase
{
    /// <summary>
    /// How often, in milliseconds, the selected Offline folder's drive is read again while the dialog refreshes.
    /// </summary>
    private const long OtherDriveProbeInterval = 10_000;

    private readonly ISender sender;
    private int modePicks;
    private readonly IPlatformServices platform;
    private readonly IMediaFileSource files;
    private readonly Localizer localizer;
    private readonly RescanRunner? rescan;
    private readonly AnalysisRunner? analysis;
    private readonly FolderWatchManager? watchers;
    private readonly MonitoringPause? monitoringPause;
    private Func<Task>? confirmed;
    private bool refreshing;
    private (Id<IndexedFolder> Id, bool Offline) otherDriveProbe;
    private long otherDriveProbedAt;
    private int otherDriveGeneration;
    private bool otherDriveProbing;
    private OtherDrive? otherDrive;

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexedFoldersViewModel"/> class.
    /// </summary>
    /// <param name="sender">Sends the folder commands.</param>
    /// <param name="platform">Folder pickers.</param>
    /// <param name="files">Finds where a picked subfolder is.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="rescan">Starts the rescan of the selected folder, or <c>null</c> where there's none.</param>
    /// <param name="analysis">Starts the analysis of the selected folder, or <c>null</c> where there's none.</param>
    /// <param name="watchers">Tells how each folder is monitored, or <c>null</c> where folders aren't monitored.</param>
    /// <param name="monitoringPause">Pauses monitoring for the whole app, or <c>null</c> where there's none.</param>
    public IndexedFoldersViewModel(ISender sender, IPlatformServices platform, IMediaFileSource files, Localizer localizer, RescanRunner? rescan = null, AnalysisRunner? analysis = null, FolderWatchManager? watchers = null, MonitoringPause? monitoringPause = null)
    {
        this.watchers = watchers;
        this.monitoringPause = monitoringPause;

        // Weak, so a dialog that closed isn't kept alive by the app-wide singletons.
        var self = new WeakReference<IndexedFoldersViewModel>(this);
        void Changed(object? sender, EventArgs e)
        {
            if (self.TryGetTarget(out var target))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = target.RefreshMonitoringAsync());
            }
        }

        if (watchers is not null)
        {
            watchers.StateChanged += Changed;
        }

        if (monitoringPause is not null)
        {
            monitoringPause.Changed += Changed;
        }
        this.rescan = rescan;
        this.analysis = analysis;
        this.sender = sender;
        this.platform = platform;
        this.files = files;
        this.localizer = localizer;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the view is hosted in the Settings "Library" page, which has its own
    /// heading and no Close button.
    /// </summary>
    [ObservableProperty]
    public partial bool IsEmbedded { get; set; }

    /// <summary>
    /// Raised when the dialog should close.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Gets a value indicating whether the dialog can pause monitoring (E04-S06-T02).
    /// </summary>
    public bool CanPauseMonitoring => monitoringPause is not null;

    /// <summary>
    /// Gets the label of the pause button: "Pause monitoring", or "Resume monitoring" while it's paused.
    /// </summary>
    public string MonitoringPauseLabel => localizer.Get(monitoringPause?.IsPaused == true ? "Monitoring_Resume" : "Monitoring_Pause");

    /// <summary>
    /// Gets the folders.
    /// </summary>
    public ObservableCollection<IndexedFolderViewModel> Folders { get; } = [];

    /// <summary>
    /// Gets a value indicating whether the library has no folders.
    /// </summary>
    public bool IsEmpty => Folders.Count == 0;

    /// <summary>
    /// Gets or sets the folder whose rules are shown.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(RulesTitle))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand), nameof(RescanCommand), nameof(AnalyzeCommand), nameof(ExcludeSubfolderCommand), nameof(ApplyRulesCommand), nameof(IndexWholeFolderCommand))]
    public partial IndexedFolderViewModel? SelectedFolder { get; set; }

    /// <summary>
    /// Gets a value indicating whether a folder is selected.
    /// </summary>
    public bool HasSelection => SelectedFolder is not null;

    /// <summary>
    /// Gets the title of the rules section.
    /// </summary>
    public string RulesTitle => SelectedFolder is { } folder ? localizer.Format("Folders_RulesTitle", folder.Name) : string.Empty;

    /// <summary>
    /// Gets the result of the last action, or why it was refused, or <c>null</c>.
    /// </summary>
    [ObservableProperty]
    public partial string? Message { get; private set; }

    /// <summary>
    /// Gets the question waiting for Confirm or Cancel, or <c>null</c>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirming))]
    public partial string? Confirmation { get; private set; }

    /// <summary>
    /// Gets the label of the confirm button.
    /// </summary>
    [ObservableProperty]
    public partial string ConfirmLabel { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether a question is waiting.
    /// </summary>
    public bool IsConfirming => Confirmation is not null;

    /// <summary>
    /// Gets the note shown when the selected Offline folder's root leads to another drive (MFC-1051), or <c>null</c>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOtherDrive))]
    [NotifyCanExecuteChangedFor(nameof(UseThisDriveCommand))]
    public partial string? OtherDriveNote { get; private set; }

    /// <summary>
    /// Gets a value indicating whether "Use this drive" is offered for the selected folder.
    /// </summary>
    public bool HasOtherDrive => OtherDriveNote is not null;

    /// <summary>
    /// Checks the folders and lists them. The dialog calls it when it opens.
    /// </summary>
    /// <returns>A task that completes when the list is shown.</returns>
    public async Task OpenAsync()
    {
        await sender.Send(new CheckFolderAvailabilityCommand());
        await RefreshAsync();
    }

    /// <summary>
    /// Lists the folders again, keeping the selection and unsaved rule edits. The dialog calls it every few seconds,
    /// so states and counts follow an import.
    /// </summary>
    /// <returns>A task that completes when the list is shown.</returns>
    public async Task RefreshAsync()
    {
        // The timer and the dialog's own actions both refresh; one at a time, or a new folder could be listed twice.
        if (refreshing)
        {
            return;
        }

        refreshing = true;
        try
        {
            var picks = modePicks;
            var summaries = await sender.Send(new GetIndexedFoldersQuery());
            var monitoring = await GetMonitoringAsync();

            // A mode picked while this read ran would be put back by it; the next refresh shows it.
            if (picks == modePicks)
            {
                Show(summaries, monitoring);
            }
        }
        finally
        {
            refreshing = false;
        }
    }

    private void Show(IReadOnlyList<IndexedFolderSummary> summaries, IReadOnlyDictionary<Id<IndexedFolder>, FolderMonitoringInfo> monitoring)
    {
        var byId = summaries.ToDictionary(summary => summary.Folder.Id);
        foreach (var gone in Folders.Where(row => !byId.ContainsKey(row.Folder.Id)).ToList())
        {
            Folders.Remove(gone);
        }

        for (var index = 0; index < summaries.Count; index++)
        {
            var summary = summaries[index];
            var row = Folders.FirstOrDefault(row => row.Folder.Id == summary.Folder.Id);
            if (row is null)
            {
                Folders.Insert(index, new IndexedFolderViewModel(summary, localizer, ChooseMode));
            }
            else
            {
                row.Update(summary);
            }
        }

        if (SelectedFolder is not null && !Folders.Contains(SelectedFolder))
        {
            SelectedFolder = null;
        }
        else if (SelectedFolder is { } selected
            && ((selected.Folder.Id, selected.IsOffline) != otherDriveProbe
                || (selected.IsOffline && !otherDriveProbing && Environment.TickCount64 - otherDriveProbedAt >= OtherDriveProbeInterval)))
        {
            // Read again when the selected folder goes Offline or Online, and now and then while it stays Offline, as a
            // cloned drive plugged in meanwhile leaves it Offline; not on every refresh.
            _ = FindOtherDriveAsync(selected);
        }

        ShowMonitoring(monitoring);
        OnPropertyChanged(nameof(IsEmpty));
    }

    // Reads how each folder is monitored. Without an answer (no library, a failing query) the rows fall back to the
    // watchers' own state.
    private async Task<IReadOnlyDictionary<Id<IndexedFolder>, FolderMonitoringInfo>> GetMonitoringAsync()
    {
        try
        {
            return (await sender.Send(new GetFolderMonitoringQuery())).ToDictionary(info => info.FolderId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new Dictionary<Id<IndexedFolder>, FolderMonitoringInfo>();
        }
    }

    private async Task RefreshMonitoringAsync() => ShowMonitoring(await GetMonitoringAsync());

    private void ShowMonitoring(IReadOnlyDictionary<Id<IndexedFolder>, FolderMonitoringInfo> monitoring)
    {
        foreach (var row in Folders)
        {
            if (monitoring.TryGetValue(row.Folder.Id, out var info))
            {
                row.ApplyMonitoring(info);
            }
            else
            {
                row.MonitoringState = watchers?.GetState(row.Folder.Id) ?? FolderMonitoringState.Off;
            }
        }

        OnPropertyChanged(nameof(MonitoringPauseLabel));
    }

    // The user picked a mode in a row. The picker is a view of the folder, so it's shown again from the library after.
    private void ChooseMode(IndexedFolderViewModel row, MonitoringMode mode)
    {
        modePicks++;
        _ = ChooseModeAsync(row, mode);
    }

    private async Task ChooseModeAsync(IndexedFolderViewModel row, MonitoringMode mode)
    {
        try
        {
            if (await sender.Send(new ChangeMonitoringModeCommand(row.Folder.Id, mode)) == ChangeMonitoringModeResult.NotFound)
            {
                Message = localizer.Format("Folders_Mode_NotFound", row.Name);
            }

            await RefreshMonitoringAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Message = localizer.Format("Folders_Mode_Failed", row.Name);
            await RefreshMonitoringAsync();
        }
    }

    /// <summary>
    /// Sets the selected folder to Poll, as the hint for a network share suggests.
    /// </summary>
    [RelayCommand]
    private void UsePoll()
    {
        if (SelectedFolder is { } row)
        {
            row.SelectedMode = row.ModeOptions.First(option => option.Mode == MonitoringMode.Poll);
        }
    }

    /// <summary>
    /// Pauses monitoring, or resumes it when it's paused (E04-S06-T02).
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ToggleMonitoringPause()
    {
        if (monitoringPause is null)
        {
            return;
        }

        if (monitoringPause.IsPaused)
        {
            monitoringPause.Resume();
        }
        else
        {
            monitoringPause.Pause();
        }

        OnPropertyChanged(nameof(MonitoringPauseLabel));
        await RefreshMonitoringAsync();
    }

    /// <summary>
    /// Gets how many folders this window has added so far, for the summary after a drop (E03-S06).
    /// </summary>
    public int AddedCount { get; private set; }

    /// <summary>
    /// Adds folders with the overlap rules (E02-S03-T02): folders that are covered already are refused with a message,
    /// and when one contains indexed folders the dialog asks before merging them. Dropping folders on the navigation
    /// panel comes here too (E13-S02).
    /// </summary>
    /// <param name="folders">The folders.</param>
    /// <returns>A task that completes when they're added, or the question is asked.</returns>
    public async Task AddFoldersAsync(IReadOnlyList<FolderHandle> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);

        var messages = new List<string>();
        var toAdd = new List<FolderHandle>();
        var merges = new List<string>();
        foreach (var folder in folders)
        {
            var overlap = await sender.Send(new CheckFolderOverlapQuery(folder));
            switch (overlap.Kind)
            {
                case FolderOverlapKind.AlreadyIndexed:
                    messages.Add(localizer.Format("Folders_AlreadyIndexed", overlap.Folders[0].DisplayPath));
                    break;
                case FolderOverlapKind.InsideIndexed:
                    messages.Add(localizer.Format("Folders_InsideIndexed", folder.Id, overlap.Folders[0].DisplayPath));
                    break;
                case FolderOverlapKind.PartialFolder:
                    // Adding a partial folder indexes it whole (E03-S05): on its own, after the same question as its button.
                    if (folders.Count == 1 && Folders.FirstOrDefault(row => row.Folder.Id == overlap.Folders[0].Id) is { } partial)
                    {
                        SelectedFolder = partial;
                        AskIndexWhole(partial);
                        return;
                    }

                    toAdd.Add(folder);
                    break;
                case FolderOverlapKind.ContainsIndexed:
                    merges.Add(localizer.Format("Folders_MergeConfirm", folder.Id, string.Join(", ", overlap.Folders.Select(child => child.DisplayPath))));
                    toAdd.Add(folder);
                    break;
                default:
                    toAdd.Add(folder);
                    break;
            }
        }

        Message = messages.Count > 0 ? string.Join(Environment.NewLine, messages) : null;
        if (toAdd.Count == 0)
        {
            return;
        }

        if (merges.Count > 0)
        {
            Ask(string.Join(Environment.NewLine, merges), localizer.Get("Folders_Merge"), () => AddAsync(toAdd));
        }
        else
        {
            await AddAsync(toAdd);
        }
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        if (await platform.PickFolderAsync(localizer.Get("Folders_PickFolder")) is { } folder)
        {
            await AddFoldersAsync([folder]);
        }
    }

    private async Task AddAsync(IReadOnlyList<FolderHandle> folders)
    {
        var result = await sender.Send(new AddLibraryFoldersCommand(folders));
        if (result.Refused.Count > 0)
        {
            // E.g. two dropped folders where one is inside the other.
            Message = string.Join(Environment.NewLine, result.Refused.Select(refused => localizer.Format("Folders_InsideIndexed", refused.Folder.Id, refused.CoveredBy.DisplayPath)));
        }

        AddedCount += result.Added.Count;
        await RefreshAsync();
        SelectedFolder = result.Added.Count > 0 ? Folders.FirstOrDefault(row => row.Folder.Id == result.Added[^1].Id) : SelectedFolder;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Remove()
    {
        if (SelectedFolder is not { } row)
        {
            return;
        }

        var count = IndexedFolderViewModel.ItemCountText(localizer, row.Summary.ItemCount + row.Summary.ExcludedCount);
        Ask(localizer.Format("Folders_RemoveConfirm", row.Path, count), localizer.Get("Folders_Remove"), async () =>
        {
            if (await sender.Send(new RemoveFolderCommand(row.Folder.Id)))
            {
                Message = localizer.Format("Folders_Removing", row.Path);
            }

            await RefreshAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RescanAsync()
    {
        if (rescan is null || SelectedFolder is not { } row)
        {
            return;
        }

        var (outcome, message) = await rescan.StartAsync(row.Folder.Id);
        Message = message ?? localizer.Format("Rescan_Started", row.Path);
        if (outcome == UseCases.Rescan.StartRescan.StartRescanOutcome.Started)
        {
            await RefreshAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task AnalyzeAsync()
    {
        if (analysis is null || SelectedFolder is not { } row)
        {
            return;
        }

        if (await analysis.StartAsync(AnalysisScope.Folder(row.Folder.Id)) is { } result)
        {
            Message = localizer.Format("Analyze_Queued", IndexedFolderViewModel.ItemCountText(localizer, result.ItemCount));
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ExcludeSubfolderAsync()
    {
        if (SelectedFolder is not { } row || await platform.PickFolderAsync(localizer.Get("Folders_PickSubfolder")) is not { } subfolder)
        {
            return;
        }

        if (files.GetPathInside(subfolder, row.Folder.ToHandle()) is { Length: > 0 } path)
        {
            row.AddExclusion(FolderRules.ForSubfolder(path));
            Message = null;
        }
        else
        {
            Message = localizer.Format("Folders_NotASubfolder", row.Path);
        }
    }

    [RelayCommand]
    private void RemoveExclusion(ExcludedSubfolderViewModel subfolder) => SelectedFolder?.RemoveExclusion(subfolder);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ApplyRulesAsync()
    {
        if (SelectedFolder is not { HasRuleChanges: true } row)
        {
            return;
        }

        var result = await sender.Send(new ChangeFolderRulesCommand(row.Folder.Id, row.EditedRules()));
        Message = localizer.Format("Folders_RulesApplied", Format.Number(result.Excluded), Format.Number(result.Restored));
        await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void IndexWholeFolder()
    {
        if (SelectedFolder is { IsPartial: true } row)
        {
            AskIndexWhole(row);
        }
    }

    private void AskIndexWhole(IndexedFolderViewModel row)
    {
        var count = IndexedFolderViewModel.ItemCountText(localizer, row.Summary.ItemCount + row.Summary.ExcludedCount);
        Ask(localizer.Format("Folders_IndexWholeConfirm", row.Path, count), localizer.Get("Folders_IndexWhole"), async () =>
        {
            await sender.Send(new IndexWholeFolderCommand(row.Folder.Id));
            await RefreshAsync();
        });
    }

    partial void OnSelectedFolderChanged(IndexedFolderViewModel? value)
    {
        otherDrive = null;
        OtherDriveNote = null;
        otherDriveProbe = default;
        if (value is not null)
        {
            _ = FindOtherDriveAsync(value);
        }
    }

    /// <summary>
    /// Finds whether the selected Offline folder's root leads to another drive, to offer "Use this drive" (MFC-1051).
    /// </summary>
    private async Task FindOtherDriveAsync(IndexedFolderViewModel row)
    {
        var generation = ++otherDriveGeneration;
        otherDriveProbe = (row.Folder.Id, row.IsOffline);
        otherDriveProbedAt = Environment.TickCount64;
        OtherDrive? drive = null;
        if (row.IsOffline)
        {
            otherDriveProbing = true;
            try
            {
                drive = await sender.Send(new FindOtherDriveQuery(row.Folder.Id));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ObjectDisposedException or OperationCanceledException)
            {
                // E.g. the library closed meanwhile: no drive is offered, and the next probe asks again.
            }
            finally
            {
                if (generation == otherDriveGeneration)
                {
                    otherDriveProbing = false;
                }
            }
        }

        // A newer probe, or a selection changed while the drive was read, has its own answer coming.
        if (generation != otherDriveGeneration || !ReferenceEquals(SelectedFolder, row))
        {
            return;
        }

        otherDriveProbing = false;
        otherDrive = drive;
        OtherDriveNote = drive switch
        {
            null => null,
            { Label: { Read: true, Label: { } label } } => localizer.Format("Folders_OtherDriveNote", row.Path, label),
            _ => localizer.Format("Folders_OtherDriveNoteNoLabel", row.Path),
        };
    }

    [RelayCommand(CanExecute = nameof(HasOtherDrive))]
    private void UseThisDrive()
    {
        if (SelectedFolder is not { } row || otherDrive is not { } drive)
        {
            return;
        }

        var count = IndexedFolderViewModel.ItemCountText(localizer, row.Summary.ItemCount + row.Summary.ExcludedCount);
        var question = drive.OtherFolders > 0
            ? localizer.Format("Folders_UseThisDriveConfirmMany", row.Path, Format.Number(drive.OtherFolders))
            : localizer.Format("Folders_UseThisDriveConfirm", row.Path, count);
        Ask(question, localizer.Get("Folders_UseThisDrive"), async () =>
        {
            var result = await sender.Send(new UseThisDriveCommand(row.Folder.Id));
            Message = result switch
            {
                { Outcome: UseThisDriveOutcome.Done, OtherFolders: > 0 } => localizer.Format("Folders_UsingThisDriveMany", row.Path, Format.Number(result.OtherFolders)),
                { Outcome: UseThisDriveOutcome.Done } => localizer.Format("Folders_UsingThisDrive", row.Path),
                { Outcome: UseThisDriveOutcome.NoOtherDrive } => localizer.Format("Folders_NoOtherDrive", row.Path),
                _ => null,
            };

            // The refresh reads the selected folder's drive again, whether or not its status changed.
            otherDriveProbe = default;
            await RefreshAsync();
        });
    }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        var action = confirmed;
        CancelConfirmation();
        if (action is not null)
        {
            await action();
        }
    }

    [RelayCommand]
    private void CancelConfirmation()
    {
        confirmed = null;
        Confirmation = null;
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Ask(string question, string confirmLabel, Func<Task> action)
    {
        Message = null;
        ConfirmLabel = confirmLabel;
        confirmed = action;
        Confirmation = question;
    }
}
