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
/// A deletion is held for <see cref="DeleteGrace"/> before it's queued (E04-S02-T02), because many tools move a file by
/// deleting it and creating it again: a file that appears meanwhile with the same content is paired with the held item
/// and relinks it. Held deletions live in memory (<see cref="HeldDeletes"/>); those held when the app quits are found by
/// the next startup sync.
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

    private readonly HeldDeletes held;
    private readonly TimeProvider clock;
    private readonly ILogger<ChangeProcessor> logger;
    private readonly IJobSystem? jobs;
    private readonly ITimer timer;
    private readonly Lock scheduling = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ChangeProcessor"/> class.
    /// </summary>
    /// <param name="held">The deletions waiting out the grace period.</param>
    /// <param name="clock">Times the delete grace period.</param>
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
    /// Queues a folder's changes. Deletions are held first.
    /// </summary>
    /// <param name="folderId">The folder.</param>
    /// <param name="changes">The changes, in the order they became stable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the changes are queued or held.</returns>
    public async Task SubmitAsync(Id<IndexedFolder> folderId, IReadOnlyList<FolderChange> changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);

        if (changes.Count == 0)
        {
            return;
        }

        if (jobs?.Current is not { } scheduler)
        {
            LogNoJobSystem(changes.Count, folderId);
            return;
        }

        var deleted = changes.Where(change => change.Kind == FolderChangeKind.Deleted).Select(change => change.RelativePath).ToList();
        if (deleted.Count > 0)
        {
            held.Hold(scheduler, folderId, deleted, clock.GetUtcNow() + DeleteGrace);
            Schedule();
            LogHeld(deleted.Count, folderId);
        }

        if (deleted.Count < changes.Count)
        {
            await EnqueueAsync(scheduler, folderId, [.. changes.Where(change => change.Kind != FolderChangeKind.Deleted)], cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Dispose() => timer.Dispose();

    // Reads the next due time and sets the timer in one step, so a hold and a release can't leave the timer late.
    private void Schedule()
    {
        lock (scheduling)
        {
            var next = held.NextDue();
            timer.Change(next is { } due ? TimeSpan.FromTicks(Math.Max(0, (due - clock.GetUtcNow()).Ticks)) : Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task ReleaseDueAsync()
    {
        var (owner, due) = held.TakeDue(clock.GetUtcNow());
        Schedule();

        // The library closed or changed meanwhile: the next startup sync finds those deletions.
        if (owner is not IJobScheduler scheduler || !ReferenceEquals(jobs?.Current, scheduler))
        {
            return;
        }

        foreach (var (folderId, paths) in due)
        {
            try
            {
                await EnqueueAsync(scheduler, folderId, [.. paths.Select(path => new FolderChange(FolderChangeKind.Deleted, path))], CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                LogReleaseFailed(exception, paths.Count, folderId);
            }
        }
    }

    private async Task EnqueueAsync(IJobScheduler scheduler, Id<IndexedFolder> folderId, IReadOnlyList<FolderChange> changes, CancellationToken cancellationToken)
    {
        var folder = await scheduler.Library.Folders.GetAsync(folderId, cancellationToken).ConfigureAwait(false);
        if (folder is null)
        {
            return;
        }

        WorkItemRequest[] work = [.. changes.Chunk(ChangesPerWorkItem)
            .Select(chunk => new WorkItemRequest(ChangeProcessingStages.Apply, Payload: ChangePayload.Serialize(chunk)))];
        await scheduler.EnqueueJobAsync(
            new JobRequest(ImportStages.ImportJob, $"Import changes in {folder.DisplayPath}", JobPriority.Import, work) { FolderId = folderId },
            cancellationToken).ConfigureAwait(false);
        LogQueued(changes.Count, folderId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} changes of watched folder {Folder} aren't processed: no library is open or there's no job system")]
    private partial void LogNoJobSystem(int count, Id<IndexedFolder> folder);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Queued {Count} changes of watched folder {Folder}")]
    private partial void LogQueued(int count, Id<IndexedFolder> folder);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Holding {Count} deleted paths of watched folder {Folder}")]
    private partial void LogHeld(int count, Id<IndexedFolder> folder);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Count} deleted paths of watched folder {Folder} couldn't be queued; the next rescan finds them")]
    private partial void LogReleaseFailed(Exception exception, int count, Id<IndexedFolder> folder);
}
