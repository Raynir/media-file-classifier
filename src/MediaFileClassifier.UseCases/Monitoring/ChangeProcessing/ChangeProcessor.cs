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
/// Gathered changes live in memory too, and the next startup sync finds those of an app that quit.
/// </para>
/// </remarks>
public sealed partial class ChangeProcessor : IDisposable
{
    /// <summary>
    /// The most changes in one work item, so a renamed folder of many files is applied in a few steps.
    /// </summary>
    public const int ChangesPerWorkItem = 1000;

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
    public ChangeProcessor(HeldDeletes held, TimeProvider clock, ILogger<ChangeProcessor> logger, IJobSystem? jobs = null)
    {
        this.held = held;
        this.clock = clock;
        this.logger = logger;
        this.jobs = jobs;
        timer = clock.CreateTimer(_ => _ = ReleaseDueAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
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

                gathering.Add(changes.Where(change => change.Kind != FolderChangeKind.Deleted), now);
            }
        }

        Schedule();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        timer.Dispose();
        enqueueing.Dispose();
    }

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
            var next = held.NextDue();
            foreach (var gathering in gathered.Values)
            {
                if (next is null || gathering.Due < next)
                {
                    next = gathering.Due;
                }
            }

            timer.Change(next is { } due ? TimeSpan.FromTicks(Math.Max(0, (due - clock.GetUtcNow()).Ticks)) : Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task ReleaseDueAsync()
    {
        var now = clock.GetUtcNow();
        var (heldFor, deletions) = held.TakeDue(now);
        object? gatheredFor;
        List<(Id<IndexedFolder> FolderId, IReadOnlyList<FolderChange> Changes)> changes;
        lock (syncRoot)
        {
            gatheredFor = owner;
            changes = [.. gathered.Where(pair => pair.Value.Due <= now).Select(pair => (pair.Key, pair.Value.Changes))];
            foreach (var (folderId, _) in changes)
            {
                gathered.Remove(folderId);
            }
        }

        Schedule();

        // The library closed or changed meanwhile: the next startup sync finds those changes.
        if (jobs?.Current is not { } scheduler)
        {
            return;
        }

        if (ReferenceEquals(gatheredFor, scheduler))
        {
            foreach (var (folderId, due) in changes)
            {
                await TryEnqueueAsync(scheduler, folderId, due).ConfigureAwait(false);
            }
        }

        if (ReferenceEquals(heldFor, scheduler))
        {
            foreach (var (folderId, paths) in deletions)
            {
                await TryEnqueueAsync(scheduler, folderId, [.. paths.Select(path => new FolderChange(FolderChangeKind.Deleted, path))]).ConfigureAwait(false);
            }
        }
    }

    private async Task TryEnqueueAsync(IJobScheduler scheduler, Id<IndexedFolder> folderId, IReadOnlyList<FolderChange> changes)
    {
        try
        {
            await EnqueueAsync(scheduler, folderId, changes, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogReleaseFailed(exception, changes.Count, folderId);
        }
    }

    // One at a time, so two releases can't both start a job for the same folder.
    private async Task EnqueueAsync(IJobScheduler scheduler, Id<IndexedFolder> folderId, IReadOnlyList<FolderChange> changes, CancellationToken cancellationToken)
    {
        await enqueueing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var folder = await scheduler.Library.Folders.GetAsync(folderId, cancellationToken).ConfigureAwait(false);
            if (folder is null)
            {
                return;
            }

            WorkItemRequest[] work = [.. changes.Chunk(ChangesPerWorkItem)
                .Select(chunk => new WorkItemRequest(ChangeProcessingStages.Apply, Payload: ChangePayload.Serialize(chunk)))];
            if (await TryAppendAsync(scheduler, folderId, work, cancellationToken).ConfigureAwait(false))
            {
                LogAppended(changes.Count, folderId);
                return;
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
        }
        finally
        {
            enqueueing.Release();
        }
    }

    // Only to a job that's waiting or running: adding work to a finished job would revive it, and a job the user paused
    // holds only what it had.
    private async Task<bool> TryAppendAsync(IJobScheduler scheduler, Id<IndexedFolder> folderId, IReadOnlyList<WorkItemRequest> work, CancellationToken cancellationToken)
    {
        Id<ProcessingJob> jobId;
        lock (syncRoot)
        {
            if (!ReferenceEquals(owner, scheduler) || !lastJobs.TryGetValue(folderId, out jobId))
            {
                return false;
            }
        }

        var job = await scheduler.Library.Jobs.GetJobAsync(jobId, cancellationToken).ConfigureAwait(false);
        return job?.State is ProcessingJobState.Queued or ProcessingJobState.Running or ProcessingJobState.WaitingForModel
            && await scheduler.EnqueueWorkAsync(jobId, work, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} changes of watched folder {Folder} aren't processed: no library is open or there's no job system")]
    private partial void LogNoJobSystem(int count, Id<IndexedFolder> folder);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Queued {Count} changes of watched folder {Folder} in a new job")]
    private partial void LogQueued(int count, Id<IndexedFolder> folder);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Added {Count} changes of watched folder {Folder} to its job")]
    private partial void LogAppended(int count, Id<IndexedFolder> folder);

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
        private DateTimeOffset last = first;

        public IReadOnlyList<FolderChange> Changes => changes;

        public DateTimeOffset Due => last + GatherQuiet < latest ? last + GatherQuiet : latest;

        public void Add(IEnumerable<FolderChange> more, DateTimeOffset now)
        {
            changes.AddRange(more);
            last = now;
        }
    }
}
