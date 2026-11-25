using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Discovery;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Import.Stages;
using MediaFileClassifier.UseCases.Libraries;
using MediaFileClassifier.UseCases.Rescan;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Monitoring.ChangeProcessing;

/// <summary>
/// Applies a batch of a watched folder's changes (E04-S02-T01) with the operations a rescan uses (E02-S04):
/// <list type="bullet">
/// <item>A new file is registered with the folder's rules. When its content matches an item whose file is gone (a
/// deletion still held, <see cref="HeldDeletes"/>, or a Missing item of any folder), the item is relinked to it
/// with the rescan's move rules (<see cref="MoveDetector"/>, E04-S02-T02); otherwise it goes on through the import's
/// stages (E03-S07).</item>
/// <item>A changed file, or a Missing item's file that's back, goes to the rescan's check stage (E02-S04-T03), which
/// keeps the user's metadata and queues re-analysis when the content changed.</item>
/// <item>A renamed or moved file relinks its item, which keeps its ID and metadata (E02-S04-T02).</item>
/// <item>A deleted file's item is marked Missing with everything kept (E02-S04-T04), if the file is still gone. When an
/// item registered for a new file in the last <see cref="CopyWindow"/> has its content, the deletion ends a move between drives, which
/// Windows does as a copy, then a delete: the old item takes the copy's file and keeps its ID and metadata, and the
/// copy's item is deleted with its thumbnails, embeddings and AI results (MFC-1052).</item>
/// </list>
/// Batches are applied one at a time, in steps of <see cref="ChangesPerStep"/>, and <see cref="ChangeProcessor"/> queues one
/// batch of a folder at a time, so the changes of one folder are applied in order.
/// </summary>
/// <param name="files">Checks that deleted files are gone.</param>
/// <param name="discovery">Registers new files.</param>
/// <param name="moves">Pairs new files with items whose file is gone.</param>
/// <param name="held">The deletions still held.</param>
/// <param name="thumbnails">Deletes the thumbnails of a copy's item that goes.</param>
/// <param name="clock">Clock for status changes.</param>
/// <param name="logger">Logger.</param>
/// <param name="tally">Remembers what the job changed, for the activity log and the notification, or <see langword="null"/> for none.</param>
/// <param name="scanGate">Keeps the registration apart from a rescan's reconcile (MFC-1040), or <see langword="null"/> where none runs.</param>
public sealed partial class ApplyChangesStage(
    IMediaFileSource files,
    IFolderDiscovery discovery,
    MoveDetector moves,
    HeldDeletes held,
    IThumbnailCache thumbnails,
    TimeProvider clock,
    ILogger<ApplyChangesStage> logger,
    MonitoringTally? tally = null,
    FolderScanGate? scanGate = null) : IStageHandler, IDisposable
{
    /// <summary>
    /// The most changes applied in one step, so a renamed folder of many files is read and written in a few steps.
    /// </summary>
    public const int ChangesPerStep = 1000;

    /// <summary>
    /// How long before a deletion an item registered for a copy of the deleted file can be (MFC-1052). A move between
    /// drives copies the whole file before the delete. The time counts from when the watcher registered the copy, which
    /// is after the file became stable. The registrations are kept in memory only, so a restart between the copy and
    /// the delete leaves the copy as a new item.
    /// </summary>
    public static readonly TimeSpan CopyWindow = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim gate = new(1, 1);

    // The items registered for new files in the last CopyWindow, with when: the only items a deletion can pair with as
    // a copy, so a duplicate an import or rescan found is never taken for one. Used under the gate only; item IDs are
    // unique across libraries, so another library's entries just aren't found.
    private readonly Dictionary<Id<MediaItem>, (DateTimeOffset At, long SizeBytes)> recentlyRegistered = [];

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
            var folders = await IndexedFolders.GetActiveAsync(library, cancellationToken).ConfigureAwait(false);
            var folder = folders.FirstOrDefault(other => other.Id == folderId);
            if (folder is null)
            {
                LogFolderGone(changes.Count, folderId);
                return;
            }

            foreach (var step in changes.Chunk(ChangesPerStep))
            {
                await ApplyAsync(context, folder, folders, step, cancellationToken).ConfigureAwait(false);
            }
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

    private async Task ApplyAsync(
        WorkItemContext context,
        IndexedFolder folder,
        IReadOnlyList<IndexedFolder> folders,
        IReadOnlyList<FolderChange> changes,
        CancellationToken cancellationToken)
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

        // Relinked first, so the paths they left are free for new items. Files may have swapped names in the batch.
        await library.Items.UpdateMovedAsync(batch.Written.Values, cancellationToken).ConfigureAwait(false);

        await EnqueueAsync(context, RescanStages.Check, batch.Check, cancellationToken).ConfigureAwait(false);
        var gone = await MarkMissingAsync(library, folder, items, batch, cancellationToken).ConfigureAwait(false);
        var copied = await PairCopiesAsync(context, folders, gone, batch.Now, cancellationToken).ConfigureAwait(false);
        var missing = gone.Count - copied;
        var registered = await RegisterAsync(library, folder, batch.New, cancellationToken).ConfigureAwait(false);
        var moved = await PairMovesAsync(context, folder, folders, registered, batch.Now, cancellationToken).ConfigureAwait(false);
        foreach (var item in registered.Where(item => !moved.Contains(item.Id)))
        {
            recentlyRegistered[item.Id] = (batch.Now, item.SizeBytes);
        }

        await EnqueueAsync(context, ImportStages.Fingerprint, registered.Select(item => item.Id).Where(itemId => !moved.Contains(itemId)), cancellationToken)
            .ConfigureAwait(false);

        tally?.Add(context.WorkItem.JobId, new MonitoringCounts(registered.Count - moved.Count, batch.Check.Count, batch.Relinked + moved.Count + copied, missing, folder.DisplayPath));
        LogApplied(folder.Id, registered.Count - moved.Count, batch.Check.Count, batch.Relinked + moved.Count + copied, missing);
    }

    // A renamed file whose item isn't known is handled as a new file at its new path. An excluded item moves and stays
    // excluded, as in a rescan.
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
        if (item is null)
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
        if (item.Status == MediaItemStatus.Excluded)
        {
            return;
        }

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
                // A Pending item is checked anyway: a batch stopped after it left Missing may not have queued its check.
                if (item.Status == MediaItemStatus.Pending || stat is null || !FolderComparison.IsSame(item, stat))
                {
                    batch.Check.Add(item.Id);
                }

                return;
        }
    }

    // Only items whose file is still gone, and never while the folder can't be reached: an unplugged drive isn't a
    // deletion (D188).
    private async Task<IReadOnlyList<MediaItem>> MarkMissingAsync(IOpenLibrary library, IndexedFolder folder, FolderItems items, Batch batch, CancellationToken cancellationToken)
    {
        if (batch.Gone.Count == 0)
        {
            return [];
        }

        if (folder.Status == FolderStatus.Offline || !await files.IsAvailableAsync(folder.ToHandle(), cancellationToken).ConfigureAwait(false))
        {
            LogFolderUnreachable(batch.Gone.Count, folder.Id);
            return [];
        }

        var missing = new List<MediaItem>();
        var seen = new HashSet<Id<MediaItem>>();
        foreach (var path in batch.Gone)
        {
            foreach (var item in await items.FindUnderAsync(path, cancellationToken).ConfigureAwait(false))
            {
                if (item.Status is MediaItemStatus.Missing or MediaItemStatus.Excluded
                    || !seen.Add(item.Id)
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

        return missing;
    }

    // The items just marked Missing whose content an item the watcher registered in the copy window has, in any folder
    // that can be reached: that item is a copy a move between drives made before deleting the file (MFC-1052). The
    // pairs follow the rescan's move rules (MoveDetector), which also fingerprint a copy the import hasn't reached yet.
    // The old item keeps its ID and metadata and takes the copy's file; the copy's item goes with all it has, and edits
    // made on it in the window go with it. Returns how many items were relinked.
    private async Task<int> PairCopiesAsync(
        WorkItemContext context,
        IReadOnlyList<IndexedFolder> folders,
        IReadOnlyList<MediaItem> missing,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var (itemId, _) in recentlyRegistered.Where(entry => entry.Value.At < now - CopyWindow).ToList())
        {
            recentlyRegistered.Remove(itemId);
        }

        var sizes = missing.Where(item => item.Hashes.Fingerprint is not null).Select(item => item.SizeBytes).ToHashSet();
        Id<MediaItem>[] candidates = [.. recentlyRegistered.Where(entry => sizes.Contains(entry.Value.SizeBytes)).Select(entry => entry.Key)];
        if (candidates.Length == 0)
        {
            return 0;
        }

        var library = context.Library;
        var online = folders.Where(other => other.Status == FolderStatus.Online).ToDictionary(other => other.Id);
        var recent = await library.Items.GetByIdsAsync(candidates, cancellationToken).ConfigureAwait(false);
        (IndexedFolder Folder, MediaItem File)[] copies = [.. recent
            .Where(item => item.Status is not (MediaItemStatus.Missing or MediaItemStatus.Excluded)
                && sizes.Contains(item.SizeBytes)
                && online.ContainsKey(item.FolderId))
            .Select(item => (online[item.FolderId], item))];
        if (copies.Length == 0)
        {
            return 0;
        }

        var pairs = await moves.PairAsync(copies, missing, cancellationToken).ConfigureAwait(false);
        var check = new List<Id<MediaItem>>();
        var paired = 0;
        foreach (var (item, folder, copy) in pairs)
        {
            // The copy's derived data goes first: its vectors leave the search index only while their rows are there.
            thumbnails.Invalidate(library.Layout, copy.Id);
            await library.Embeddings.RemoveForItemsAsync([copy.Id], cancellationToken).ConfigureAwait(false);
            await library.Metadata.RemoveAiResultsAsync([copy.Id], cancellationToken).ConfigureAwait(false);

            item.Relocate(folder.Id, copy.Path);
            item.UpdateFile(copy.SizeBytes, copy.FileCreatedAt, copy.FileModifiedAt, item.Hashes);
            item.ChangeStatus(MediaItemStatus.Pending, now);
            if (await library.Items.ReplaceWithMovedAsync(item, copy.Id, cancellationToken).ConfigureAwait(false))
            {
                recentlyRegistered.Remove(copy.Id);
                check.Add(item.Id);
                paired++;

                // The copy's import may still have been running: a thumbnail it wrote after the first call is dropped too.
                // Its other stages find no item and end (or fail on the missing row and are not recorded).
                thumbnails.Invalidate(library.Layout, copy.Id);
                LogCopyPaired(item.Id, copy.Id);
            }
            else
            {
                // Changed meanwhile, e.g. the copy was removed or the old file came back: the old item stays as stored.
                // The copy, if it's still there, lost its derived data, so it goes through the import again.
                LogCopyNotPaired(item.Id, copy.Id);
                if (await library.Items.GetAsync(copy.Id, cancellationToken).ConfigureAwait(false) is { } survivor
                    && survivor.Status is MediaItemStatus.Indexed or MediaItemStatus.Error)
                {
                    survivor.ChangeStatus(MediaItemStatus.Pending, now);
                    await library.Items.UpdateAsync(survivor, cancellationToken).ConfigureAwait(false);
                    check.Add(survivor.Id);
                }
            }
        }

        await EnqueueAsync(context, RescanStages.Check, check, cancellationToken).ConfigureAwait(false);
        return paired;
    }

    private async Task<IReadOnlyList<MediaItem>> RegisterAsync(IOpenLibrary library, IndexedFolder folder, List<RelativePath> paths, CancellationToken cancellationToken)
    {
        if (paths.Count == 0)
        {
            return [];
        }

        // A rescan reconciling the library meanwhile would register the same paths (MFC-1040).
        IReadOnlyList<FileRegistration> registrations;
        using (scanGate is null ? null : await scanGate.EnterSharedAsync(cancellationToken).ConfigureAwait(false))
        {
            registrations = await discovery.RegisterFilesAsync(library, folder, [.. paths.Select(path => path.Value).Distinct(StringComparer.Ordinal)], cancellationToken)
                .ConfigureAwait(false);
        }

        return [.. registrations.Where(registration => registration.Outcome == FileRegistrationOutcome.Registered).Select(registration => registration.Item!)];
    }

    // A new file with the content of an item whose file is gone is that file, moved: the item takes its place and keeps
    // its ID and metadata, and the item just registered for it goes. Returns the new items that went.
    private async Task<IReadOnlySet<Id<MediaItem>>> PairMovesAsync(
        WorkItemContext context,
        IndexedFolder folder,
        IReadOnlyList<IndexedFolder> folders,
        IReadOnlyList<MediaItem> registered,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (registered.Count == 0)
        {
            return new HashSet<Id<MediaItem>>();
        }

        var library = context.Library;
        var gone = await GetGoneItemsAsync(library, folders, cancellationToken).ConfigureAwait(false);
        if (gone.Count == 0)
        {
            return new HashSet<Id<MediaItem>>();
        }

        var pairs = await moves.PairAsync([.. registered.Select(item => (folder, item))], gone, cancellationToken).ConfigureAwait(false);
        var removed = new HashSet<Id<MediaItem>>();
        var check = new List<Id<MediaItem>>();
        foreach (var (item, _, found) in pairs)
        {
            // The new item goes first, so the moved one can take its path.
            await library.Items.DeleteAsync(found.Id, cancellationToken).ConfigureAwait(false);
            removed.Add(found.Id);
            item.Relocate(folder.Id, found.Path);
            item.UpdateFile(found.SizeBytes, found.FileCreatedAt, found.FileModifiedAt, item.Hashes);
            if (item.Status == MediaItemStatus.Missing)
            {
                item.ChangeStatus(MediaItemStatus.Pending, now);
                check.Add(item.Id);
            }

            await library.Items.UpdateAsync(item, cancellationToken).ConfigureAwait(false);
        }

        await EnqueueAsync(context, RescanStages.Check, check, cancellationToken).ConfigureAwait(false);
        return removed;
    }

    // Missing items of any folder, and the items of held deletions whose file is still gone, excluded ones included: a
    // moved excluded file stays excluded. Items of a folder that can't be reached aren't gone.
    private async Task<IReadOnlyList<MediaItem>> GetGoneItemsAsync(IOpenLibrary library, IReadOnlyList<IndexedFolder> folders, CancellationToken cancellationToken)
    {
        var gone = (await library.Items.GetByStatusAsync(MediaItemStatus.Missing, cancellationToken: cancellationToken).ConfigureAwait(false)).ToList();
        var seen = gone.Select(item => item.Id).ToHashSet();
        foreach (var byFolder in held.GetAll().GroupBy(entry => entry.FolderId))
        {
            if (folders.FirstOrDefault(other => other.Id == byFolder.Key) is not { Status: FolderStatus.Online } heldFolder)
            {
                continue;
            }

            var items = await FolderItems.CreateAsync(library, heldFolder, 0, cancellationToken).ConfigureAwait(false);
            foreach (var (_, heldPath) in byFolder)
            {
                if (ToPath(heldPath) is not { } path)
                {
                    continue;
                }

                foreach (var item in await items.FindUnderAsync(path, cancellationToken).ConfigureAwait(false))
                {
                    if (item.Status != MediaItemStatus.Missing
                        && !seen.Contains(item.Id)
                        && await files.GetStatAsync(ItemStage.GetLocation(heldFolder, item), cancellationToken).ConfigureAwait(false) is null)
                    {
                        seen.Add(item.Id);
                        gone.Add(item);
                    }
                }
            }
        }

        return gone;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Changes of watched folder {Folder}: {New} new, {Checked} to check, {Relinked} relinked, {Missing} missing")]
    private partial void LogApplied(Id<IndexedFolder> folder, int @new, int @checked, int relinked, int missing);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} changes aren't applied: folder {Folder} was removed")]
    private partial void LogFolderGone(int count, Id<IndexedFolder> folder);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} deleted paths aren't marked: folder {Folder} can't be reached")]
    private partial void LogFolderUnreachable(int count, Id<IndexedFolder> folder);

    [LoggerMessage(Level = LogLevel.Information, Message = "Item {Item} was moved between drives: it takes the file of item {Copy}, which is deleted")]
    private partial void LogCopyPaired(Id<MediaItem> item, Id<MediaItem> copy);

    [LoggerMessage(Level = LogLevel.Information, Message = "Item {Item} isn't paired with the copy {Copy}: one of them changed meanwhile")]
    private partial void LogCopyNotPaired(Id<MediaItem> item, Id<MediaItem> copy);

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
