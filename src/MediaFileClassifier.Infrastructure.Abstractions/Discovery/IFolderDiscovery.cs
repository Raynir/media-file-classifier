using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;

namespace MediaFileClassifier.Infrastructure.Abstractions.Discovery;

/// <summary>
/// Finds the supported media in an indexed folder and registers new files as Discovered items (E03-S02). It's the
/// first step of an import (MFC-69) and of a rescan (E02-S04).
/// </summary>
public interface IFolderDiscovery
{
    /// <summary>
    /// Scans a folder with its rules, and registers the media files that aren't in the library yet. Items are written
    /// in batches as they're found, so they show up while the scan goes on.
    /// </summary>
    /// <param name="library">The open library the folder belongs to.</param>
    /// <param name="folder">The folder to scan.</param>
    /// <param name="progress">Receives the counts after each listed folder and each written batch.</param>
    /// <param name="registered">
    /// Called with each batch of new items once it's written, before the scan goes on; the import pipeline queues their
    /// next stage here (E03-S07).
    /// </param>
    /// <param name="cancellationToken">Cancellation token. Batches already written stay written.</param>
    /// <returns>What the scan found.</returns>
    /// <exception cref="DirectoryNotFoundException">The folder doesn't exist, e.g. its drive isn't connected.</exception>
    Task<DiscoveryResult> DiscoverAsync(
        IOpenLibrary library,
        IndexedFolder folder,
        IProgress<DiscoveryProgress>? progress = null,
        Func<IReadOnlyList<MediaItem>, CancellationToken, Task>? registered = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers files the user selected in a folder, without scanning it (E03-S05): each file is looked up and its
    /// content decides its format, as in <see cref="DiscoverAsync"/>. A full folder's rules still apply; a partial
    /// folder (D012) has none. New items are written in batches and returned as Discovered; queuing them is the caller's.
    /// </summary>
    /// <param name="library">The open library the folder belongs to.</param>
    /// <param name="folder">The folder the files are in.</param>
    /// <param name="relativePaths">The files' paths relative to the folder root, with <c>/</c> separators.</param>
    /// <param name="cancellationToken">Cancellation token. Batches already written stay written.</param>
    /// <returns>What happened to each distinct path, in the order given.</returns>
    Task<IReadOnlyList<FileRegistration>> RegisterFilesAsync(
        IOpenLibrary library,
        IndexedFolder folder,
        IReadOnlyList<string> relativePaths,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans a folder with its rules like <see cref="DiscoverAsync"/>, but registers nothing: it returns the files at
    /// known paths and the new media files, for a rescan to reconcile (E02-S04-T01).
    /// </summary>
    /// <param name="library">The open library the folder belongs to.</param>
    /// <param name="folder">The folder to scan.</param>
    /// <param name="progress">Receives the counts after each listed folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The folder's files.</returns>
    /// <exception cref="DirectoryNotFoundException">The folder doesn't exist, e.g. its drive isn't connected.</exception>
    Task<FolderSnapshot> SnapshotAsync(
        IOpenLibrary library,
        IndexedFolder folder,
        IProgress<DiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
