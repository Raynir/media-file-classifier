using MediaFileClassifier.Infrastructure.Abstractions.Files;

namespace MediaFileClassifier.Infrastructure.Abstractions.Platform;

/// <summary>
/// Tells which volume a folder is on, and finds a folder whose volume came back under another drive letter
/// (E04-S04-T03). Windows identifies a volume by its file system's serial number; a platform without drive letters
/// uses the no-op default, which knows no volume.
/// </summary>
public interface IVolumeLocator
{
    /// <summary>
    /// Gets the identity of the volume a folder is on.
    /// </summary>
    /// <param name="folder">The folder, which should be reachable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The identity, or <see langword="null"/> when it can't be read or the folder isn't on a local volume,
    /// e.g. a network share or a mapped network drive.</returns>
    Task<string?> GetVolumeIdAsync(FolderHandle folder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a folder on its volume when the volume is mounted under another root than the folder's, e.g. a drive that
    /// was <c>E:</c> and came back as <c>F:</c>.
    /// </summary>
    /// <param name="folder">The folder at its stored root.</param>
    /// <param name="volumeId">The identity of the volume it was on.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The folder at its new root, or <see langword="null"/> when exactly one other mounted volume has that
    /// identity and the folder exists on it isn't the case.</returns>
    Task<FolderHandle?> FindOnVolumeAsync(FolderHandle folder, string volumeId, CancellationToken cancellationToken = default);
}
