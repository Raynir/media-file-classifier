using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Libraries.UseThisDrive;

/// <summary>
/// Another drive at an Offline folder's root (MFC-1051): the folder's path can be listed, but on a volume with another
/// identity than the stored one, e.g. a drive cloned to a new disk or reformatted and restored.
/// </summary>
/// <param name="VolumeId">The identity of the drive at the folder's root now.</param>
/// <param name="Label">The drive's label, if read, and <see langword="null"/> for none.</param>
public sealed record OtherDrive(string VolumeId, (bool Read, string? Label) Label)
{
    /// <summary>
    /// Finds another drive at an Offline folder's root.
    /// </summary>
    /// <param name="files">Lists the folder's root and reads its label.</param>
    /// <param name="volumes">Reads the drive's identity, or <see langword="null"/> where the platform has none.</param>
    /// <param name="folder">The folder.</param>
    /// <param name="logger">Logs a label that couldn't be read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The drive, or <see langword="null"/> when the folder isn't Offline with a stored identity, its root
    /// can't be listed, or the drive there is its own or can't be identified.</returns>
    internal static async Task<OtherDrive?> FindAsync(IMediaFileSource files, IVolumeLocator? volumes, IndexedFolder folder, ILogger logger, CancellationToken cancellationToken)
    {
        if (volumes is null || folder.Status != FolderStatus.Offline || folder.VolumeId is not { } stored)
        {
            return null;
        }

        var handle = folder.ToHandle();
        if (!await files.IsAvailableAsync(handle, cancellationToken).ConfigureAwait(false)
            || await FolderReachability.ReadVolumeIdAsync(volumes, folder, cancellationToken).ConfigureAwait(false) is not { } found
            || string.Equals(found, stored, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new OtherDrive(found, await VolumeLabels.TryReadAsync(files, handle, logger, cancellationToken).ConfigureAwait(false));
    }
}
