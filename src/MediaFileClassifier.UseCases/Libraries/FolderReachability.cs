using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;

namespace MediaFileClassifier.UseCases.Libraries;

/// <summary>
/// Tells whether a reachable folder is still on its own volume (E04-S04-T03). A drive letter that another drive took
/// while the folder's drive was away leads to that other drive; it's never scanned as the folder, so nothing is marked
/// Missing or imported from it.
/// </summary>
internal static class FolderReachability
{
    /// <summary>
    /// Checks that a folder's root is on the volume stored for it. Without a stored identity, or one that can't be read
    /// now, the folder counts as on its volume, as before identities were stored.
    /// </summary>
    /// <param name="volumes">Reads the volume identity, or <see langword="null"/> where the platform has none.</param>
    /// <param name="folder">The folder, whose root can be listed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="false"/> when the root is on another volume.</returns>
    public static async Task<bool> IsOnItsVolumeAsync(IVolumeLocator? volumes, IndexedFolder folder, CancellationToken cancellationToken)
    {
        if (volumes is null || folder.VolumeId is not { } expected)
        {
            return true;
        }

        return await ReadVolumeIdAsync(volumes, folder, cancellationToken).ConfigureAwait(false) is not { } actual
            || string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the identity of the volume a folder's root is on.
    /// </summary>
    /// <param name="volumes">Reads the identity.</param>
    /// <param name="folder">The folder, whose root can be listed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The identity, or <see langword="null"/> when it can't be read.</returns>
    public static async Task<string?> ReadVolumeIdAsync(IVolumeLocator volumes, IndexedFolder folder, CancellationToken cancellationToken)
    {
        try
        {
            return await volumes.GetVolumeIdAsync(folder.ToHandle(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
