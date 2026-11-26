using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.UseCases.Import;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Monitoring.ChangeProcessing;

/// <summary>
/// Queues a watched folder's changes (E04-S02) in the job system, as <see cref="ChangeProcessingStages.Apply"/> work in
/// an import job of the folder at import priority. Nothing is applied here, so processing that's paused (E05-S05) holds
/// the changes too.
/// </summary>
/// <remarks>
/// <para>
/// A deletion is held for <see cref="DeleteGrace"/> before it's queued (E04-S02-T02), because many tools move a file by
/// deleting it and creating it again: a file that appears meanwhile with the same content is paired with the held item
/// and relinks it. Held deletions live in memory (<see cref="HeldDeletes"/>); those held when the app quits are found by
/// the next startup sync.
/// </para>
/// <para>
/// A burst of changes, such as a copy of many files, makes one job (E04-S02-T04): the other changes of a folder are
/// gathered until none came for <see cref="GatherQuiet"/>, or for <see cref="GatherMax"/> at most, and then join the
/// folder's monitoring job while it's unfinished. A job that finished isn't revived; the next changes start a new one.
/// A folder has one batch of changes queued at a time, so its batches are applied in order: changes that are due while
/// one waits or runs go on gathering. Released deletions join the gathering too. Gathered changes live in memory, and
/// the next startup sync finds those of an app that quit.
/// </para>
/// </remarks>
public sealed partial class ChangeProcessor : IDisposable
{
    /// <summary>
    /// How long a deleted path waits before its items are marked Missing.
    /// </summary>
    public static readonly TimeSpan DeleteGrace = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a folder must have no new changes before its gathered changes are queued.
    /// </summary>
    public static readonly TimeSpan GatherQuiet = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The longest a change is gathered while more keep coming.
    /// </summary>
    public static readonly TimeSpan GatherMax = TimeSpan.FromSeconds(30);

    private readonly HeldDeletes held;
    private readonly TimeProvider clock;
    private readonly ILogger<ChangeProcessor> logger;
    private readonly IJobSystem? jobs;
    private readonly MonitoringPause? pause;
    private readonly ITimer timer;
    private readonly Lock syncRoot = new();
    private readonly SemaphoreSlim enqueueing = new(1, 1);

    // The scheduler the gathered changes and the jobs below belong to: they mean nothing in another library.
    private readonly Dictionary<Id<IndexedFolder>, Gathering> gathered = [];
    private readonly Dictionary<Id<IndexedFolder>, Id<ProcessingJob>> lastJobs = [];
    private object? owner;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChangeProcessor"/> class.
    /// </summary>
    /// <param name="held">The deletions waiting out the grace period.</param>
    /// <param name="clock">Times the delete grace period and the gathering.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="jobs">Runs the jobs of the open library, if the platform has background processing.</param>
    /// <param name="pause">Holds the changes while monitoring is paused (E04-S06-T02), or <see langword="null"/> for no pause.</param>
    public ChangeProcessor(HeldDeletes held, TimeProvider clock, ILogger<ChangeProcessor> logger, IJobSystem? jobs = null, MonitoringPause? pause = null)
    {
        this.pause = pause;
        this.held = held;
        this.clock = clock;
        this.logger = logger;
        this.jobs = jobs;
        timer = clock.CreateTimer(_ => _ = ReleaseDueAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        if (pause is not null)
        {
            pause.Changed += OnPauseChanged;
        }
    }

    /// <summary>
    /// Takes a folder's changes. Deletions are held, the others gathered, before they're queued.
    /// </summary>
    /// <param name="folderId">The folder.</param>
    /// <param name="changes">The changes, in the order they became stable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the changes are held or gathered.</returns>
    public Task SubmitAsync(Id<IndexedFolder> folderId, IReadOnlyList<FolderChange> changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        cancellationToken.ThrowIfCancellationRequested();

        if (changes.Count == 0)
        {
            return Task.CompletedTask;
        }

        if (jobs?.Current is not { } scheduler)
        {
            LogNoJobSystem(changes.Count, folderId);
            return Task.CompletedTask;
        }

        var now = clock.GetUtcNow();
        var deleted = changes.Where(change => change.Kind == FolderChangeKind.Deleted).Select(change => change.RelativePath).ToList();
        if (deleted.Count > 0)
        {
            held.Hold(scheduler, folderId, deleted, now + DeleteGrace);
            LogHeld(deleted.Count, folderId);
        }

        if (deleted.Count < changes.Count)
        {
            lock (syncRoot)
            {
                Own(scheduler);
                if (!gathered.TryGetValue(folderId, out var gathering))
                {
                    gathered[folderId] = gathering = new Gathering(now);
                }

                gathering.Add(changes.Where(change => change.Kind != FolderChangeKind.Deleted), now + GatherQuiet);
            }
        }

        Schedule();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (pause is not null)
        {
            pause.Changed -= OnPauseChanged;
        }

        timer.Dispose();
        enqueueing.Dispose();
    }

    // Resuming releases what came due while monitoring was paused; pausing stops the timer.
    private void OnPauseChanged(object? sender, EventArgs e) => Schedule();

    // Forgets what belongs to another library.
    private void Own(IJobScheduler scheduler)
    {
        if (!ReferenceEquals(owner, scheduler))
        {
            gathered.Clear();
            lastJobs.Clear();
            owner = scheduler;
        }
    }

    // Reads the next due time and sets the timer in one step, so a submit and a release can't leave the timer late.
    private void Schedule()
    {
        lock (syncRoot)
        {
            if (pause?.IsPaused == true)
            {
                // Everything waits; resuming schedules it again.
                SetTimer(Timeout.InfiniteTimeSpan);
                return;
            }

            var next = held.NextDue();
            foreach (var gathering in gathered.Values)
            {
                if (next is null || gathering.Due < next)
                {
                    next = gathering.Due;
                }
            }

            SetTimer(next is { } due ? TimeSpan.FromTicks(Math.Max(0, (due - clock.GetUtcNow()).Ticks)) : Timeout.InfiniteTimeSpan);
        }
    }

    // A pause or resume may reach the timer while the processor is being disposed.
    private void SetTimer(TimeSpan dueTime)
    {
        try
        {
            timer.Change(dueTime, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Shutting down.
        }
    }

    private async Task ReleaseDueAsync()
    {
        if (pause?.IsPaused == true)
        {
            return;
        }

        // The library closed or changed meanwhile: the next startup sync finds those changes.
        var scheduler = jobs?.Current;
        var now = clock.GetUtcNow();
        var (heldFor, deletions) = held.TakeDue(now);
        List<(Id<IndexedFolder> FolderId, IReadOnlyList<FolderChange> Changes)> due;
        lock (syncRoot)
        {
            if (scheduler is not null && ReferenceEquals(heldFor, scheduler))
            {
                Own(scheduler);
                foreach (var (folderId, paths) in deletions)
                {
                    if (!gathered.TryGetValue(folderId, out var gathering))
                    {
                        gathered[folderId] = gathering = new Gathering(now);
                    }

                    gathering.Add(paths.Select(path => new FolderChange(FolderChangeKind.Deleted, path)), now);
                }
            }

            due = scheduler is not null && ReferenceEquals(owner, scheduler)
                ? [.. gathered.Where(pair => pair.Value.Due <= now).Select(pair => (pair.Key, pair.Value.Changes))]
                : [];
            foreach (var (folderId, _) in due)
            {
                gathered.Remove(folderId);
            }
        }

        foreach (var (folderId, changes) in due)
        {
            await FlushAsync(scheduler!, folderId, changes).ConfigureAwait(false);
        }

        Schedule();
    }

    private async Task FlushAsync(IJobScheduler scheduler, Id<IndexedFolder> folderId, IReadOnlyList<FolderChange> changes)
    {
        try
        {
            if (await EnqueueAsync(scheduler, folderId, changes, CancellationToken.None).ConfigureAwait(false))
            {
                return;
            }

            // The folder's previous batch is still waiting or running: these go back to the gathering, before any newer.
            lock (syncRoot)
            {
                if (!ReferenceEquals(owner, scheduler))
                {
                    return;
                }

                var now = clock.GetUtcNow();
                var waiting = new Gathering(now);
                waiting.Add(changes, now + GatherQuiet);
                if (gathered.TryGetValue(folderId, out var newer))
                {
                    waiting.Add(newer.Changes, newer.Due);
                }

                gathered[folderId] = waiting;
            }

            LogWaiting(changes.Count, folderId);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogReleaseFailed(exception, changes.Count, folderId);
        }
    }

    // One at a time, so two releases can't both start a job for the same folder. Returns false, and queues nothing,
    // while the folder's previous batch hasn't been applied.
    private async Task<bool> EnqueueAsync(IJobScheduler scheduler, Id<IndexedFolder> folderId, IReadOnlyList<FolderChange> changes, CancellationToken cancellationToken)
    {
        await enqueueing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var folder = await scheduler.Library.Folders.GetAsync(folderId, cancellationToken).ConfigureAwait(false);
            if (folder is null)
            {
                return true;
            }

            var job = await GetLastJobAsync(scheduler, folderId, cancellationToken).ConfigureAwait(false);
            if (job is not null && !ProcessingJobStateTransitions.IsFinished(job.State)
                && (await scheduler.Library.Jobs.GetWorkItemsAsync(job.Id, cancellationToken).ConfigureAwait(false))
                    .Any(item => item.Stage == ChangeProcessingStages.Apply && item.State is WorkItemState.Queued or WorkItemState.Running))
            {
                return false;
            }

            WorkItemRequest[] work = [new(ChangeProcessingStages.Apply, Payload: ChangePayload.Serialize(changes))];

            // Only to a job that's waiting or running, checked in the same write (MFC-1054): adding work to a finished
            // job would revive it and announce its end twice, and a job the user paused holds only what it had.
            if (job is not null && await scheduler.AppendWorkAsync(job.Id, work, cancellationToken).ConfigureAwait(false))
            {
                LogAppended(changes.Count, folderId);
                return true;
            }

            var jobId = await scheduler.EnqueueJobAsync(
                new JobRequest(ImportStages.ImportJob, $"Import changes in {folder.DisplayPath}", JobPriority.Import, work) { FolderId = folderId },
                cancellationToken).ConfigureAwait(false);
            lock (syncRoot)
            {
                Own(scheduler);
                lastJobs[folderId] = jobId;
            }

            LogQueued(changes.Count, folderId);
            return true;
        }
        finally
        {
            enqueueing.Release();
        }
    }

    private async Task<ProcessingJob?> GetLastJobAsync(IJobScheduler scheduler, Id<IndexedFolder> folderId, CancellationToken cancellationToken)
    {
        Id<ProcessingJob>? known;
        lock (syncRoot)
        {
            if (!ReferenceEquals(owner, scheduler))
            {
                return null;
            }

            known = lastJobs.TryGetValue(folderId, out var jobId) ? jobId : null;
        }

        if (known is { } id)
        {
            return await scheduler.Library.Jobs.GetJobAsync(id, cancellationToken).ConfigureAwait(false);
        }

        // Not known in memory, e.g. after a restart (MFC-1054): the folder's newest unfinished job that applies watched
        // changes, so a new batch waits for its batches or joins it rather than running beside them.
        var jobs = scheduler.Library.Jobs;
        foreach (var job in (await jobs.GetUnfinishedJobsAsync(cancellationToken).ConfigureAwait(false))
            .Where(job => job.FolderId == folderId && job.Type == ImportStages.ImportJob)
            .OrderByDescending(job => job.CreatedAt))
        {
            if ((await jobs.GetWorkItemsAsync(job.Id, cancellationToken).ConfigureAwait(false)).Any(item => item.Stage == ChangeProcessingStages.Apply))
            {
                lock (syncRoot)
                {
                    if (ReferenceEquals(owner, scheduler))
                    {
                        lastJobs.TryAdd(folderId, job.Id);
                    }
                }

                return job;
            }
        }

        return null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} changes of watched folder {Folder} aren't processed: no library is open or there's no job system")]
    private partial void LogNoJobSystem(int count, Id<IndexedFolder> folder);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Queued {Count} changes of watched folder {Folder} in a new job")]
    private partial void LogQueued(int count, Id<IndexedFolder> folder);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Added {Count} changes of watched folder {Folder} to its job")]
    private partial void LogAppended(int count, Id<IndexedFolder> folder);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Count} changes of watched folder {Folder} wait for its previous batch")]
    private partial void LogWaiting(int count, Id<IndexedFolder> folder);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Holding {Count} deleted paths of watched folder {Folder}")]
    private partial void LogHeld(int count, Id<IndexedFolder> folder);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Count} changes of watched folder {Folder} couldn't be queued; the next rescan finds them")]
    private partial void LogReleaseFailed(Exception exception, int count, Id<IndexedFolder> folder);

    /// <summary>
    /// A folder's changes waiting to be queued.
    /// </summary>
    /// <param name="first">When the first change came.</param>
    private sealed class Gathering(DateTimeOffset first)
    {
        private readonly List<FolderChange> changes = [];
        private readonly DateTimeOffset latest = first + GatherMax;

        public IReadOnlyList<FolderChange> Changes => changes;

        public DateTimeOffset Due { get; private set; } = first + GatherMax;

        // Due no later than the longest gather from the first change.
        public void Add(IEnumerable<FolderChange> more, DateTimeOffset due)
        {
            changes.AddRange(more);
            Due = due < latest ? due : latest;
        }
    }
}
