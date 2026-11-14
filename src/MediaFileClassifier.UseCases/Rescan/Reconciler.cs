using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Discovery;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;

namespace MediaFileClassifier.UseCases.Rescan;

/// <summary>
/// The reconciliation engine (E02-S04-T01): lists folders, compares them with the library's items, and classifies each
/// file as new, unchanged, modified, moved or missing. It writes nothing; <see cref="ReconcileStage"/> applies the result.
/// </summary>
/// <param name="discovery">Lists the folders.</param>
/// <param name="moves">Pairs new files with disappeared items (E02-S04-T02).</param>
public sealed class Reconciler(IFolderDiscovery discovery, MoveDetector moves)
{
    /// <summary>
    /// Reconciles folders of a library. All of them are listed before anything is classified as moved or missing, so a
    /// file moved between two of them is found. New files are also paired with items already Missing in other folders.
    /// </summary>
    /// <param name="library">The open library.</param>
    /// <param name="folders">The folders to rescan.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The classified files.</returns>
    public async Task<Reconciliation> ReconcileAsync(IOpenLibrary library, IReadOnlyList<IndexedFolder> folders, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(folders);

        var scanned = new List<(IndexedFolder Folder, DiscoveryResult Result)>();
        var offline = new List<IndexedFolder>();
        var newFiles = new List<(IndexedFolder Folder, MediaItem File)>();
        var unchanged = new List<MediaItem>();
        var modified = new List<ModifiedFile>();
        var disappeared = new List<MediaItem>();
        foreach (var folder in folders)
        {
            FolderSnapshot snapshot;
            try
            {
                snapshot = await discovery.SnapshotAsync(library, folder, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (DirectoryNotFoundException)
            {
                offline.Add(folder);
                continue;
            }

            var items = await library.Items.GetByFolderAsync(folder.Id, cancellationToken).ConfigureAwait(false);
            var comparison = FolderComparison.Compare(folder, items, snapshot);
            scanned.Add((folder, snapshot.Result));
            newFiles.AddRange(comparison.New.Select(file => (folder, file)));
            unchanged.AddRange(comparison.Unchanged);
            modified.AddRange(comparison.Modified);
            disappeared.AddRange(comparison.Disappeared);
        }

        // Relinked before anything is marked Missing (E02-S04-T02).
        var scannedIds = scanned.Select(entry => entry.Folder.Id).ToHashSet();
        var missingElsewhere = (await library.Items.GetByStatusAsync(MediaItemStatus.Missing, cancellationToken: cancellationToken).ConfigureAwait(false))
            .Where(item => !scannedIds.Contains(item.FolderId));
        IReadOnlyList<MovedFile> moved = newFiles.Count == 0
            ? []
            : await moves.PairAsync(newFiles, [.. disappeared, .. missingElsewhere], cancellationToken).ConfigureAwait(false);
        var movedFiles = moved.Select(pair => pair.Found.Id).ToHashSet();
        var movedItems = moved.Select(pair => pair.Item.Id).ToHashSet();

        return new Reconciliation
        {
            Scanned = scanned,
            Offline = offline,
            New = [.. newFiles.Select(entry => entry.File).Where(file => !movedFiles.Contains(file.Id))],
            Unchanged = unchanged,
            Modified = modified,
            Moved = moved,
            Missing = [.. disappeared.Where(item => !movedItems.Contains(item.Id))],
        };
    }
}
