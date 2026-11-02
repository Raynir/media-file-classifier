using MediaFileClassifier.Domain.Libraries;
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
    /// <param name="cancellationToken">Cancellation token. Batches already written stay written.</param>
    /// <returns>What the scan found.</returns>
    /// <exception cref="DirectoryNotFoundException">The folder doesn't exist, e.g. its drive isn't connected.</exception>
    Task<DiscoveryResult> DiscoverAsync(
        IOpenLibrary library,
        IndexedFolder folder,
        IProgress<DiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
