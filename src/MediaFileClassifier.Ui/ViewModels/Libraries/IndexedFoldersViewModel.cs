using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
using MediaFileClassifier.UseCases.Monitoring;
using MediatR;

namespace MediaFileClassifier.Ui.ViewModels.Libraries;

/// <summary>
/// The indexed folders dialog (E02-S03-T06): the open library's folders with their state and item counts, and adding,
/// removing and the exclusion rules. Merging folders and removing one are confirmed in the dialog first.
/// </summary>
public sealed partial class IndexedFoldersViewModel : ViewModelBase
{
    private readonly ISender sender;
    private readonly IPlatformServices platform;
    private readonly IMediaFileSource files;
    private readonly Localizer localizer;
    private readonly RescanRunner? rescan;
    private readonly AnalysisRunner? analysis;
    private readonly FolderWatchManager? watchers;
    private readonly MonitoringPause? monitoringPause;
    private Func<Task>? confirmed;
    private bool refreshing;

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
            Show(await sender.Send(new GetIndexedFoldersQuery()));
        }
        finally
        {
            refreshing = false;
        }
    }

    private void Show(IReadOnlyList<IndexedFolderSummary> summaries)
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
                Folders.Insert(index, new IndexedFolderViewModel(summary, localizer));
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

        ShowMonitoring();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void ShowMonitoring()
    {
        foreach (var row in Folders)
        {
            row.MonitoringState = watchers?.GetState(row.Folder.Id) ?? FolderMonitoringState.Off;
        }

        OnPropertyChanged(nameof(MonitoringPauseLabel));
    }

    /// <summary>
    /// Pauses monitoring, or resumes it when it's paused (E04-S06-T02).
    /// </summary>
    [RelayCommand]
    private void ToggleMonitoringPause()
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

        ShowMonitoring();
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
