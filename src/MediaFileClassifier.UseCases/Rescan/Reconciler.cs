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
public sealed class Reconciler(IFolderDiscovery discovery)
{
    /// <summary>
    /// Reconciles folders of a library. All of them are listed before anything is classified as moved or missing, so a
    /// file moved between two of them is found.
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
        var newFiles = new List<MediaItem>();
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
            newFiles.AddRange(comparison.New);
            unchanged.AddRange(comparison.Unchanged);
            modified.AddRange(comparison.Modified);
            disappeared.AddRange(comparison.Disappeared);
        }

        return new Reconciliation
        {
            Scanned = scanned,
            Offline = offline,
            New = newFiles,
            Unchanged = unchanged,
            Modified = modified,
            Missing = disappeared,
        };
    }
}
