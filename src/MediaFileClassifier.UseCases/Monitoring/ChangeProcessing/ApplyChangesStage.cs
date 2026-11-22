using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Discovery;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Import.Stages;
using MediaFileClassifier.UseCases.Libraries;
using MediaFileClassifier.UseCases.Rescan;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Monitoring.ChangeProcessing;

/// <summary>
/// Applies a batch of a watched folder's changes (E04-S02-T01) with the operations a rescan uses (E02-S04):
/// <list type="bullet">
/// <item>A new file is registered with the folder's rules and goes on through the import's stages (E03-S07).</item>
/// <item>A changed file, or a Missing item's file that's back, goes to the rescan's check stage (E02-S04-T03), which
/// keeps the user's metadata and queues re-analysis when the content changed.</item>
/// <item>A renamed or moved file relinks its item, which keeps its ID and metadata (E02-S04-T02).</item>
/// <item>A deleted file's item is marked Missing with everything kept (E02-S04-T04), if the file is still gone.</item>
/// </list>
/// Batches are applied one at a time, so the changes of one folder never race each other.
/// </summary>
/// <param name="files">Checks that deleted files are gone.</param>
/// <param name="discovery">Registers new files.</param>
/// <param name="clock">Clock for status changes.</param>
/// <param name="logger">Logger.</param>
public sealed partial class ApplyChangesStage(
    IMediaFileSource files,
    IFolderDiscovery discovery,
    TimeProvider clock,
    ILogger<ApplyChangesStage> logger) : IStageHandler, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <inheritdoc />
    public string Stage => ChangeProcessingStages.Apply;

    /// <inheritdoc />
    public ResourcePool Pool => ResourcePool.Io;

    /// <inheritdoc />
    public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var changes = ChangePayload.Deserialize(context.WorkItem.Payload);
        var library = context.Library;
        var job = await library.Jobs.GetJobAsync(context.WorkItem.JobId, cancellationToken).ConfigureAwait(false);
        if (changes.Count == 0 || job?.FolderId is not { } folderId)
        {
            return;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Removed, or being removed, since the changes were queued: nothing of it is changed any more.
            var folder = (await IndexedFolders.GetActiveAsync(library, cancellationToken).ConfigureAwait(false)).FirstOrDefault(other => other.Id == folderId);
            if (folder is null)
            {
                LogFolderGone(changes.Count, folderId);
                return;
            }

            await ApplyAsync(context, folder, changes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => gate.Dispose();

    private static RelativePath? ToPath(string path)
    {
        try
        {
            return RelativePath.From(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static async Task EnqueueAsync(WorkItemContext context, string stage, IEnumerable<Id<MediaItem>> itemIds, CancellationToken cancellationToken)
    {
        WorkItemRequest[] work = [.. itemIds.Distinct().Select(itemId => new WorkItemRequest(stage, itemId))];
        if (work.Length > 0)
        {
            await context.Scheduler.EnqueueWorkAsync(context.WorkItem.JobId, work, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ApplyAsync(WorkItemContext context, IndexedFolder folder, IReadOnlyList<FolderChange> changes, CancellationToken cancellationToken)
    {
        var library = context.Library;
        var items = await FolderItems.CreateAsync(library, folder, changes.Count, cancellationToken).ConfigureAwait(false);
        var batch = new Batch(clock.GetUtcNow());
        foreach (var change in changes)
        {
            if (ToPath(change.RelativePath) is not { } path)
            {
                LogUnusablePath(change.RelativePath);
                continue;
            }

            switch (change.Kind)
            {
                case FolderChangeKind.Renamed when change.OldRelativePath is not null && ToPath(change.OldRelativePath) is { } oldPath:
                    await RenameAsync(folder, items, batch, oldPath, path, change.Stat, cancellationToken).ConfigureAwait(false);
                    break;
                case FolderChangeKind.Deleted:
                    batch.Gone.Add(path);
                    break;
                default:
                    await ChangeAsync(items, batch, path, change.Stat, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }

        // Relinked first, so the paths they left are free for new items.
        foreach (var written in batch.Written.Values.Chunk(ReconcileStage.BatchSize))
        {
            await library.Items.UpdateRangeAsync(written, cancellationToken).ConfigureAwait(false);
        }

        await EnqueueAsync(context, RescanStages.Check, batch.Check, cancellationToken).ConfigureAwait(false);
        var missing = await MarkMissingAsync(library, folder, items, batch, cancellationToken).ConfigureAwait(false);
        var registered = await RegisterAsync(library, folder, batch.New, cancellationToken).ConfigureAwait(false);
        await EnqueueAsync(context, ImportStages.Fingerprint, registered.Select(item => item.Id), cancellationToken).ConfigureAwait(false);

        LogApplied(folder.Id, registered.Count, batch.Check.Count, batch.Relinked, missing);
    }

    // A renamed file whose item isn't known is handled as a new file at its new path.
    private async Task RenameAsync(
        IndexedFolder folder,
        FolderItems items,
        Batch batch,
        RelativePath oldPath,
        RelativePath path,
        MediaFileStat? stat,
        CancellationToken cancellationToken)
    {
        var item = await items.FindAsync(oldPath, cancellationToken).ConfigureAwait(false);
        if (item is null || item.Status == MediaItemStatus.Excluded)
        {
            await ChangeAsync(items, batch, path, stat, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Moved over another item's file: that item's file changed, and the moved one's old path is gone.
        if (await items.FindAsync(path, cancellationToken).ConfigureAwait(false) is { } replaced && replaced.Id != item.Id)
        {
            await ChangeAsync(items, batch, path, stat, cancellationToken).ConfigureAwait(false);
            batch.Gone.Add(oldPath);
            return;
        }

        item.Relocate(folder.Id, path);
        items.Moved(item, oldPath);
        batch.Relinked++;
        batch.Written[item.Id] = item;
        if (item.Status == MediaItemStatus.Missing)
        {
            item.ChangeStatus(MediaItemStatus.Pending, batch.Now);
            batch.Check.Add(item.Id);
        }
        else if (stat is not null && !FolderComparison.IsSame(item, stat))
        {
            batch.Check.Add(item.Id);
        }
    }

    private async Task ChangeAsync(FolderItems items, Batch batch, RelativePath path, MediaFileStat? stat, CancellationToken cancellationToken)
    {
        var item = await items.FindAsync(path, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            batch.New.Add(path);
            return;
        }

        switch (item.Status)
        {
            case MediaItemStatus.Excluded:
                return;
            case MediaItemStatus.Missing:
                // Back at its path: Pending until the check stage decides whether its content changed.
                item.ChangeStatus(MediaItemStatus.Pending, batch.Now);
                batch.Written[item.Id] = item;
                batch.Check.Add(item.Id);
                return;
            default:
                if (stat is null || !FolderComparison.IsSame(item, stat))
                {
                    batch.Check.Add(item.Id);
                }

                return;
        }
    }

    // Only items whose file is still gone, and never while the folder can't be reached: an unplugged drive isn't a
    // deletion (D188).
    private async Task<int> MarkMissingAsync(IOpenLibrary library, IndexedFolder folder, FolderItems items, Batch batch, CancellationToken cancellationToken)
    {
        if (batch.Gone.Count == 0)
        {
            return 0;
        }

        if (folder.Status == FolderStatus.Offline || !await files.IsAvailableAsync(folder.ToHandle(), cancellationToken).ConfigureAwait(false))
        {
            LogFolderUnreachable(batch.Gone.Count, folder.Id);
            return 0;
        }

        var missing = new List<MediaItem>();
        foreach (var path in batch.Gone)
        {
            foreach (var item in await items.FindUnderAsync(path, cancellationToken).ConfigureAwait(false))
            {
                if (item.Status is MediaItemStatus.Missing or MediaItemStatus.Excluded
                    || missing.Contains(item)
                    || await files.GetStatAsync(ItemStage.GetLocation(folder, item), cancellationToken).ConfigureAwait(false) is not null)
                {
                    continue;
                }

                item.ChangeStatus(MediaItemStatus.Missing, batch.Now);
                missing.Add(item);
            }
        }

        foreach (var written in missing.Chunk(ReconcileStage.BatchSize))
        {
            await library.Items.UpdateRangeAsync(written, cancellationToken).ConfigureAwait(false);
        }

        return missing.Count;
    }

    private async Task<IReadOnlyList<MediaItem>> RegisterAsync(IOpenLibrary library, IndexedFolder folder, List<RelativePath> paths, CancellationToken cancellationToken)
    {
        if (paths.Count == 0)
        {
            return [];
        }

        var registrations = await discovery.RegisterFilesAsync(library, folder, [.. paths.Select(path => path.Value).Distinct(StringComparer.Ordinal)], cancellationToken)
            .ConfigureAwait(false);
        return [.. registrations.Where(registration => registration.Outcome == FileRegistrationOutcome.Registered).Select(registration => registration.Item!)];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Changes of watched folder {Folder}: {New} new, {Checked} to check, {Relinked} relinked, {Missing} missing")]
    private partial void LogApplied(Id<IndexedFolder> folder, int @new, int @checked, int relinked, int missing);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} changes aren't applied: folder {Folder} was removed")]
    private partial void LogFolderGone(int count, Id<IndexedFolder> folder);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} deleted paths aren't marked: folder {Folder} can't be reached")]
    private partial void LogFolderUnreachable(int count, Id<IndexedFolder> folder);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A change to {Path} is skipped: the library can't store the path")]
    private partial void LogUnusablePath(string path);

    /// <summary>
    /// What a batch does, collected before it's written.
    /// </summary>
    private sealed class Batch(DateTimeOffset now)
    {
        public DateTimeOffset Now { get; } = now;

        public Dictionary<Id<MediaItem>, MediaItem> Written { get; } = [];

        public List<Id<MediaItem>> Check { get; } = [];

        public List<RelativePath> New { get; } = [];

        public List<RelativePath> Gone { get; } = [];

        public int Relinked { get; set; }
    }
}
