using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Libraries;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Rescan;

/// <summary>
/// The first stage of a rescan job (E02-S04): reconciles the job's folder, or every folder of the library, and applies
/// the result. New files go on through the import's stages in this job; nothing is deleted.
/// </summary>
/// <param name="reconciler">The reconciliation engine.</param>
/// <param name="clock">Clock for status changes.</param>
/// <param name="logger">Logger.</param>
public sealed partial class ReconcileStage(Reconciler reconciler, TimeProvider clock, ILogger<ReconcileStage> logger) : IStageHandler
{
    /// <summary>
    /// Number of items written per transaction, the writer's batch size (D072).
    /// </summary>
    public const int BatchSize = 100;

    /// <inheritdoc />
    public string Stage => RescanStages.Reconcile;

    /// <inheritdoc />
    public ResourcePool Pool => ResourcePool.Io;

    /// <inheritdoc />
    public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var library = context.Library;
        var jobId = context.WorkItem.JobId;
        var job = await library.Jobs.GetJobAsync(jobId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The rescan job isn't in the library.");
        var folders = await GetFoldersAsync(library, job, cancellationToken).ConfigureAwait(false);

        var result = await reconciler.ReconcileAsync(library, folders, cancellationToken).ConfigureAwait(false);
        if (await UpdateFolderStatusAsync(library, result, cancellationToken).ConfigureAwait(false))
        {
            // The work parked while those folders were Offline runs now (D025).
            context.Scheduler.ReleaseParked();
        }

        var newlyMissing = await ApplyAsync(context, result, cancellationToken).ConfigureAwait(false);

        // Stored last: the job can't finish before this work item does, so a finished job always has its counts.
        var counts = new RescanCounts(folders.Count, result.Offline.Count, result.New.Count, result.Moved.Count, newlyMissing, result.Modified.Count, result.Unchanged.Count);
        await library.Jobs.SetResultAsync(jobId, counts.ToJson(), cancellationToken).ConfigureAwait(false);

        LogReconciled(folders.Count, result.Offline.Count, result.New.Count, result.Modified.Count, result.Moved.Count, result.Missing.Count, result.Unchanged.Count);
    }

    // The job's folder, or all of them; a folder that's being removed isn't rescanned.
    private static async Task<IReadOnlyList<IndexedFolder>> GetFoldersAsync(IOpenLibrary library, ProcessingJob job, CancellationToken cancellationToken)
    {
        var removing = (await library.Jobs.GetUnfinishedJobsAsync(cancellationToken).ConfigureAwait(false))
            .Where(other => other.Type == IndexedFolders.RemoveJob && other.FolderId is not null)
            .Select(other => other.FolderId!.Value)
            .ToHashSet();
        IReadOnlyList<IndexedFolder> folders = job.FolderId is { } folderId
            ? [.. new[] { await library.Folders.GetAsync(folderId, cancellationToken).ConfigureAwait(false) }.OfType<IndexedFolder>()]
            : await library.Folders.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return [.. folders.Where(folder => !removing.Contains(folder.Id))];
    }

    private static async Task EnqueueAsync(IJobScheduler scheduler, Id<ProcessingJob> jobId, string stage, IEnumerable<Id<MediaItem>> itemIds, CancellationToken cancellationToken)
    {
        WorkItemRequest[] work = [.. itemIds.Select(itemId => new WorkItemRequest(stage, itemId))];
        if (work.Length > 0)
        {
            await scheduler.EnqueueWorkAsync(jobId, work, cancellationToken).ConfigureAwait(false);
        }
    }

    // Offline folders are skipped and marked (D188); a listed folder is online again. Returns whether one came online.
    private async Task<bool> UpdateFolderStatusAsync(IOpenLibrary library, Reconciliation result, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        foreach (var folder in result.Offline.Where(folder => folder.Status != FolderStatus.Offline))
        {
            folder.MarkOffline(now);
            await library.Folders.UpdateAvailabilityAsync(folder, cancellationToken).ConfigureAwait(false);
        }

        var cameOnline = false;
        foreach (var (folder, _) in result.Scanned.Where(scanned => scanned.Folder.Status == FolderStatus.Offline))
        {
            folder.MarkOnline(now);
            await library.Folders.UpdateAvailabilityAsync(folder, cancellationToken).ConfigureAwait(false);
            cameOnline = true;
        }

        return cameOnline;
    }

    private async Task<int> ApplyAsync(WorkItemContext context, Reconciliation result, CancellationToken cancellationToken)
    {
        var library = context.Library;
        var jobId = context.WorkItem.JobId;
        var now = clock.GetUtcNow();

        // The items were read when the folders were listed. One the user removed since then stays removed (E02-S10):
        // it's left out here rather than written back with its old status.
        Id<MediaItem>[] listed = [.. result.Moved.Select(pair => pair.Item.Id), .. result.Modified.Select(file => file.Item.Id), .. result.Missing.Select(item => item.Id)];
        var removedSince = listed.Length == 0
            ? []
            : (await library.Items.GetByIdsAsync(listed, cancellationToken).ConfigureAwait(false))
                .Where(item => item.Status == MediaItemStatus.Excluded)
                .Select(item => item.Id)
                .ToHashSet();

        // Moved or renamed: the item keeps its ID and all its metadata (E02-S04-T02).
        var moved = new List<MediaItem>();
        var check = new List<MediaItem>();
        foreach (var (item, folder, found) in result.Moved.Where(pair => !removedSince.Contains(pair.Item.Id)))
        {
            item.Relocate(folder.Id, found.Path);
            item.UpdateFile(found.SizeBytes, found.FileCreatedAt, found.FileModifiedAt, item.Hashes);
            if (item.Status == MediaItemStatus.Missing)
            {
                item.ChangeStatus(MediaItemStatus.Pending, now);
                check.Add(item);
            }

            moved.Add(item);
        }

        // A user-removed item follows its file without a status change, so it stays out of sight (E02-S10-T02).
        foreach (var (item, folder, found) in result.MovedExcluded)
        {
            item.Relocate(folder.Id, found.Path);
            item.UpdateFile(found.SizeBytes, found.FileCreatedAt, found.FileModifiedAt, item.Hashes);
            moved.Add(item);
        }

        foreach (var batch in moved.Chunk(BatchSize))
        {
            await library.Items.UpdateRangeAsync(batch, cancellationToken).ConfigureAwait(false);
        }

        // Changed or back: the check stage fingerprints them (E02-S04-T03). A returning item is Pending meanwhile.
        foreach (var (item, _) in result.Modified.Where(file => !removedSince.Contains(file.Item.Id)))
        {
            if (item.Status == MediaItemStatus.Missing)
            {
                item.ChangeStatus(MediaItemStatus.Pending, now);
                await library.Items.UpdateAsync(item, cancellationToken).ConfigureAwait(false);
            }

            check.Add(item);
        }

        await EnqueueAsync(context.Scheduler, jobId, RescanStages.Check, check.Select(item => item.Id), cancellationToken).ConfigureAwait(false);

        // Gone: marked Missing with everything kept, so the item can be relinked if the file comes back (E02-S04-T04).
        MediaItem[] missing = [.. result.Missing.Where(item => item.Status != MediaItemStatus.Missing && !removedSince.Contains(item.Id))];
        foreach (var item in missing)
        {
            item.ChangeStatus(MediaItemStatus.Missing, now);
        }

        foreach (var batch in missing.Chunk(BatchSize))
        {
            await library.Items.UpdateRangeAsync(batch, cancellationToken).ConfigureAwait(false);
        }

        // New files are imported like any other (E03-S07): the fingerprint stage goes on from what's known.
        foreach (var batch in result.New.Chunk(BatchSize))
        {
            await library.Items.AddRangeAsync(batch, cancellationToken).ConfigureAwait(false);
            await EnqueueAsync(context.Scheduler, jobId, ImportStages.Fingerprint, batch.Select(item => item.Id), cancellationToken).ConfigureAwait(false);
        }

        return missing.Length;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Rescan of {Folders} folders ({Offline} offline): {New} new, {Modified} modified, {Moved} moved, {Missing} missing, {Unchanged} unchanged")]
    private partial void LogReconciled(int folders, int offline, int @new, int modified, int moved, int missing, int unchanged);
}
