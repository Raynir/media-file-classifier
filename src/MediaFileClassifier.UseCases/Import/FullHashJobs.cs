using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;

namespace MediaFileClassifier.UseCases.Import;

/// <summary>
/// Queues full hashing for imported items in their folder's <see cref="ImportStages.FullHashJob"/> job, at the low
/// full-hashing priority, so it never slows an import down (E01-S06-T02). Each folder has at most one unfinished hash
/// job; a new one is created when there's none.
/// </summary>
public sealed class FullHashJobs : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<Id<IndexedFolder>, Id<ProcessingJob>> jobs = [];
    private IJobScheduler? scheduler;

    /// <summary>
    /// Queues the content hash stage for items of one folder.
    /// </summary>
    /// <param name="scheduler">The open library's scheduler.</param>
    /// <param name="folder">The items' folder.</param>
    /// <param name="itemIds">The items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the work is queued.</returns>
    public async Task EnqueueAsync(IJobScheduler scheduler, IndexedFolder folder, IReadOnlyCollection<Id<MediaItem>> itemIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(itemIds);

        if (itemIds.Count == 0)
        {
            return;
        }

        List<WorkItemRequest> work = [.. itemIds.Select(itemId => new WorkItemRequest(ImportStages.ContentHash, itemId))];
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The IDs belong to one library's scheduler; opening another library starts over.
            if (!ReferenceEquals(this.scheduler, scheduler))
            {
                this.scheduler = scheduler;
                jobs.Clear();
            }

            var jobId = await FindAsync(scheduler, folder.Id, cancellationToken).ConfigureAwait(false);

            // A hash job that finished goes back to the queue with the new work; a cancelled one doesn't take any.
            if (jobId is { } existing && await scheduler.EnqueueWorkAsync(existing, work, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            jobs[folder.Id] = await scheduler.EnqueueJobAsync(
                new JobRequest(ImportStages.FullHashJob, $"Hash {folder.DisplayPath}", JobPriority.FullHashing, work) { FolderId = folder.Id },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => gate.Dispose();

    private async Task<Id<ProcessingJob>?> FindAsync(IJobScheduler scheduler, Id<IndexedFolder> folderId, CancellationToken cancellationToken)
    {
        if (jobs.TryGetValue(folderId, out var known))
        {
            return known;
        }

        // After a restart, the folder's unfinished hash job is in the database.
        var unfinished = await scheduler.Library.Jobs.GetUnfinishedJobsAsync(cancellationToken).ConfigureAwait(false);
        return unfinished
            .Where(job => job.Type == ImportStages.FullHashJob && job.FolderId == folderId)
            .OrderBy(job => job.CreatedAt)
            .Select(job => (Id<ProcessingJob>?)job.Id)
            .FirstOrDefault();
    }
}
