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
/// <param name="OtherFolders">How many other Offline folders from the same old drive lead to this drive too, and take
/// it with the folder.</param>
public sealed record OtherDrive(string VolumeId, (bool Read, string? Label) Label, int OtherFolders = 0)
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

    /// <summary>
    /// Finds another drive at an Offline folder's root, and the library's other Offline folders from the same old drive
    /// whose roots lead to that same drive now, e.g. every folder of a cloned disk.
    /// </summary>
    /// <param name="files">Lists the folders' roots and reads the label.</param>
    /// <param name="volumes">Reads the drives' identities, or <see langword="null"/> where the platform has none.</param>
    /// <param name="folder">The folder.</param>
    /// <param name="all">Every folder of the library.</param>
    /// <param name="logger">Logs a label that couldn't be read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The drive, with how many other folders it covers, and those folders; or <see langword="null"/> when
    /// the folder's root doesn't lead to another drive.</returns>
    internal static async Task<(OtherDrive Drive, IReadOnlyList<IndexedFolder> Others)?> FindWithOthersAsync(
        IMediaFileSource files, IVolumeLocator? volumes, IndexedFolder folder, IReadOnlyList<IndexedFolder> all, ILogger logger, CancellationToken cancellationToken)
    {
        if (await FindAsync(files, volumes, folder, logger, cancellationToken).ConfigureAwait(false) is not { } drive)
        {
            return null;
        }

        var others = new List<IndexedFolder>();
        foreach (var other in all.Where(other => other.Id != folder.Id
            && other.Status == FolderStatus.Offline
            && string.Equals(other.VolumeId, folder.VolumeId, StringComparison.OrdinalIgnoreCase)))
        {
            if (await FindAsync(files, volumes, other, logger, cancellationToken).ConfigureAwait(false) is { } found
                && string.Equals(found.VolumeId, drive.VolumeId, StringComparison.OrdinalIgnoreCase))
            {
                others.Add(other);
            }
        }

        return (drive with { OtherFolders = others.Count }, others);
    }
}
