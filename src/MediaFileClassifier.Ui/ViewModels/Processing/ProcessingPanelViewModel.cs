using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Processing;
using MediaFileClassifier.UseCases.Browsing.ItemLocation;
using MediaFileClassifier.UseCases.Import.RetryFailedItems;
using MediaFileClassifier.UseCases.Processing.CancelJob;
using MediaFileClassifier.UseCases.Processing.GetJobFailures;
using MediaFileClassifier.UseCases.Processing.GetProcessingQueue;
using MediaFileClassifier.UseCases.Processing.SetJobPaused;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Ui.ViewModels.Processing;

/// <summary>
/// The processing panel (E05-S03-T01): the library's jobs in three lists, Active, Queued and Recent, with live
/// progress. The panel only looks at jobs: opening and closing it never changes them.
/// </summary>
public sealed partial class ProcessingPanelViewModel : ViewModelBase
{
    private readonly ISender sender;
    private readonly ProgressFeed feed;
    private readonly Localizer localizer;
    private readonly IPlatformServices? platform;
    private readonly ProcessingPause pause;
    private readonly ILogger logger;
    private readonly Dictionary<Id<ProcessingJob>, ProcessingJobViewModel> rows = [];
    private int version;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessingPanelViewModel"/> class.
    /// </summary>
    /// <param name="sender">Gets the jobs.</param>
    /// <param name="feed">The throttled job progress.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="platform">Shows files in the file manager, or <see langword="null"/> for no Reveal file.</param>
    /// <param name="logger">Logger, or <see langword="null"/> for none.</param>
    /// <param name="jobs">The job system, for Resume when the library opened with processing paused, or <see langword="null"/> for none.</param>
    /// <param name="pause">Pauses and resumes processing and pools (E05-S05), or <see langword="null"/> for one of the panel's own over <paramref name="jobs"/>.</param>
    public ProcessingPanelViewModel(
        ISender sender,
        ProgressFeed feed,
        Localizer localizer,
        IPlatformServices? platform = null,
        ILogger<ProcessingPanelViewModel>? logger = null,
        IJobSystem? jobs = null,
        ProcessingPause? pause = null)
    {
        this.pause = pause ?? new ProcessingPause(feed, jobs, action => action());
        this.pause.Changed += (_, _) => UpdatePause();
        localizer.Changed += (_, _) => UpdatePause();
        this.sender = sender;
        this.feed = feed;
        this.localizer = localizer;
        this.platform = platform;
        this.logger = logger ?? NullLogger<ProcessingPanelViewModel>.Instance;
        UpdatePause();
    }

    /// <summary>
    /// The number of unfinished work items from which cancelling a job asks for confirmation (E05-S03-T02).
    /// </summary>
    public const int LargeJobThreshold = 100;

    /// <summary>
    /// Raised when the panel asks to be closed.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Gets the running jobs.
    /// </summary>
    public ObservableCollection<ProcessingJobViewModel> Active { get; } = [];

    /// <summary>
    /// Gets the jobs that wait to start: queued, waiting for a model, or paused.
    /// </summary>
    public ObservableCollection<ProcessingJobViewModel> Queued { get; } = [];

    /// <summary>
    /// Gets the jobs that finished last.
    /// </summary>
    public ObservableCollection<ProcessingJobViewModel> Recent { get; } = [];

    /// <summary>
    /// Gets a value indicating whether there are no jobs to list.
    /// </summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether all processing waits for Resume: paused by the user (E05-S05), or because the
    /// library opened with unfinished work and the settings say to ask first (E05-S01-T05).
    /// </summary>
    [ObservableProperty]
    public partial bool IsPaused { get; private set; }

    /// <summary>
    /// Gets a value indicating whether all processing or any pool is paused, so the banner with Resume shows.
    /// </summary>
    [ObservableProperty]
    public partial bool IsAnythingPaused { get; private set; }

    /// <summary>
    /// Gets what the banner says is paused, or <see langword="null"/> when nothing is.
    /// </summary>
    [ObservableProperty]
    public partial string? PausedText { get; private set; }

    /// <summary>
    /// Gets the text of the header's pause button: Pause all, or Resume all while anything is paused.
    /// </summary>
    [ObservableProperty]
    public partial string PauseAllText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether disk work (the I/O pool) is paused on its own.
    /// </summary>
    [ObservableProperty]
    public partial bool IsIoPaused { get; private set; }

    /// <summary>
    /// Gets a value indicating whether processor work (the CPU pool) is paused on its own.
    /// </summary>
    [ObservableProperty]
    public partial bool IsCpuPaused { get; private set; }

    /// <summary>
    /// Gets a value indicating whether AI analysis (the AI pool) is paused on its own.
    /// </summary>
    [ObservableProperty]
    public partial bool IsAiPaused { get; private set; }

    /// <summary>
    /// Starts following the job progress; call it when the panel opens.
    /// </summary>
    public void Attach() => feed.Updated += OnProgress;

    /// <summary>
    /// Stops following the job progress; call it when the panel closes.
    /// </summary>
    public void Detach() => feed.Updated -= OnProgress;

    /// <summary>
    /// Lists the jobs again.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the lists are up to date.</returns>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var current = ++version;
        UpdatePause();
        ProcessingQueue queue;
        try
        {
            queue = await sender.Send(new GetProcessingQueueQuery(), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            // The library closed under us; the next refresh tries again.
            LogRefreshFailed(exception);
            return;
        }

        // A newer refresh already asked, so this answer may be stale.
        if (current != version)
        {
            return;
        }

        var live = feed.Current.Jobs.ToDictionary(job => job.JobId);
        var active = new List<ProcessingJobViewModel>();
        var queued = new List<ProcessingJobViewModel>();
        var recent = new List<ProcessingJobViewModel>();
        var seen = new HashSet<Id<ProcessingJob>>();
        foreach (var job in queue.Unfinished.Concat(queue.Recent))
        {
            seen.Add(job.Id);
            if (!rows.TryGetValue(job.Id, out var row))
            {
                row = rows[job.Id] = new ProcessingJobViewModel(job, localizer, this);
            }
            else
            {
                row.Update(job);
            }

            row.Update(job.IsFinished ? null : live.GetValueOrDefault(job.Id));
            (job.IsFinished ? recent : job.State == ProcessingJobState.Running ? active : queued).Add(row);
        }

        foreach (var gone in rows.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            rows.Remove(gone);
        }

        Reconcile(Active, active);
        Reconcile(Queued, queued);
        Reconcile(Recent, recent);
        IsEmpty = rows.Count == 0;

        foreach (var row in rows.Values.Where(row => row.IsShowingFailures).ToList())
        {
            await row.LoadFailuresAsync();
        }
    }

    /// <summary>
    /// Pauses or resumes one job (E05-S05-T02) and lists the jobs again.
    /// </summary>
    /// <param name="row">The job's row.</param>
    /// <param name="paused">Whether the job should be paused.</param>
    /// <returns>A task that completes when the lists are up to date.</returns>
    internal async Task SetJobPausedAsync(ProcessingJobViewModel row, bool paused)
    {
        try
        {
            await sender.Send(new SetJobPausedCommand(row.Id, paused));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            LogActionFailed(exception);
        }

        await RefreshAsync();
    }

    /// <summary>
    /// Resumes all processing and every paused pool, and lists the jobs again. Jobs paused one by one stay paused.
    /// </summary>
    /// <returns>A task that completes when the lists are up to date.</returns>
    [RelayCommand]
    private Task ResumeAsync()
    {
        pause.ResumeAll();
        return RefreshAsync();
    }

    /// <summary>
    /// The header's main pause button: pauses all processing, or resumes everything while anything is paused.
    /// </summary>
    [RelayCommand]
    private void TogglePauseAll()
    {
        pause.Toggle();
        UpdatePause();
    }

    /// <summary>
    /// Pauses a pool, or resumes it if it's paused.
    /// </summary>
    /// <param name="pool">The pool.</param>
    [RelayCommand]
    private void TogglePool(ResourcePool pool)
    {
        pause.SetPoolPaused(pool, !pause.IsPoolPaused(pool));
        UpdatePause();
    }

    private void UpdatePause()
    {
        IsPaused = pause.IsPaused;
        IsIoPaused = pause.IsPoolPaused(ResourcePool.Io);
        IsCpuPaused = pause.IsPoolPaused(ResourcePool.Cpu);
        IsAiPaused = pause.IsPoolPaused(ResourcePool.Ai);
        IsAnythingPaused = IsPaused || IsIoPaused || IsCpuPaused || IsAiPaused;
        PauseAllText = localizer.Get(IsAnythingPaused ? "Processing_ResumeAll" : "Processing_PauseAll");
        var pools = pause.PausedPools;
        PausedText = IsPaused
            ? localizer.Get("Processing_Paused")
            : pools.Count > 0
                ? localizer.Format("Processing_PoolsPaused", string.Join(", ", pools.Select(pool => localizer.Get("Processing_Pool_" + pool))))
                : null;
    }

    // Moves, adds and removes rows, so the rows that stay keep their place and state in the list.
    private static void Reconcile(ObservableCollection<ProcessingJobViewModel> target, List<ProcessingJobViewModel> desired)
    {
        for (var i = 0; i < desired.Count; i++)
        {
            var index = target.IndexOf(desired[i]);
            if (index < 0)
            {
                target.Insert(i, desired[i]);
            }
            else if (index != i)
            {
                target.Move(index, i);
            }
        }

        while (target.Count > desired.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    /// <summary>
    /// Cancels a job and lists the jobs again.
    /// </summary>
    /// <param name="row">The job's row.</param>
    /// <returns>A task that completes when the lists are up to date.</returns>
    internal async Task CancelJobAsync(ProcessingJobViewModel row)
    {
        try
        {
            await sender.Send(new CancelJobCommand(row.Id));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            LogActionFailed(exception);
        }

        await RefreshAsync();
    }

    /// <summary>
    /// Queues the failed items of a job again and lists the jobs again.
    /// </summary>
    /// <param name="row">The job's row.</param>
    /// <returns>A task that completes when the lists are up to date.</returns>
    internal async Task RetryFailedAsync(ProcessingJobViewModel row)
    {
        try
        {
            await sender.Send(new RetryFailedItemsCommand(JobId: row.Id));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            LogActionFailed(exception);
        }

        await RefreshAsync();
    }

    /// <summary>
    /// Gets the failures of a job that still need attention.
    /// </summary>
    /// <param name="row">The job's row.</param>
    /// <returns>The failures, or none when the library closed.</returns>
    internal async Task<IReadOnlyList<FailedItemViewModel>> GetFailuresAsync(ProcessingJobViewModel row)
    {
        try
        {
            var failures = await sender.Send(new GetJobFailuresQuery(row.Id));
            var canReveal = platform?.CanRevealInFileExplorer == true;
            return [.. failures.Select(failure => new FailedItemViewModel(failure, localizer, this, canReveal, row.Description))];
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            LogActionFailed(exception);
            return [];
        }
    }

    /// <summary>
    /// Queues one failed item again and lists the jobs again.
    /// </summary>
    /// <param name="failure">The failure.</param>
    /// <returns>A task that completes when the lists are up to date.</returns>
    internal async Task RetryItemAsync(FailedItemViewModel failure)
    {
        if (failure.Failure.ItemId is not { } itemId)
        {
            return;
        }

        try
        {
            await sender.Send(new RetryFailedItemsCommand(ItemIds: [itemId]));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            LogActionFailed(exception);
        }

        await RefreshAsync();
    }

    /// <summary>
    /// Shows a failed item's file in the file manager.
    /// </summary>
    /// <param name="failure">The failure.</param>
    /// <returns>A task that completes when the file manager was asked.</returns>
    internal async Task RevealItemAsync(FailedItemViewModel failure)
    {
        if (platform is null || failure.Failure.ItemId is not { } itemId)
        {
            return;
        }

        try
        {
            var locations = await sender.Send(new GetItemLocationsQuery([itemId]));
            if (locations.FirstOrDefault() is { Availability: ItemAvailability.Available } location)
            {
                await platform.RevealInFileExplorerAsync(location.Location);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or NotSupportedException)
        {
            // The file went away since the last scan, or there is no file manager.
            LogActionFailed(exception);
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnProgress(object? sender, ProgressSummary summary)
    {
        var unfinished = Active.Concat(Queued).Select(row => row.Id).ToHashSet();
        if (!unfinished.SetEquals(summary.Jobs.Select(job => job.JobId)))
        {
            // A job started, finished or was queued: the lists change, not just the numbers.
            _ = RefreshAsync();
            return;
        }

        foreach (var progress in summary.Jobs)
        {
            rows[progress.JobId].Update(progress);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A processing panel action couldn't run")]
    private partial void LogActionFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The processing panel couldn't list the jobs")]
    private partial void LogRefreshFailed(Exception exception);
}
