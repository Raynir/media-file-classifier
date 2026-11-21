using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Errors;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;

namespace MediaFileClassifier.UseCases.Libraries;

/// <summary>
/// Keeps per-item work away from Offline folders (D188, D025): the stages throw <see cref="FolderOfflineException"/>,
/// and the scheduler parks the work until the folder is Online again, without failing the item.
/// </summary>
public static class OfflineFolders
{
    /// <summary>
    /// Throws when the folder is marked Offline, before any file is touched.
    /// </summary>
    /// <param name="folder">The item's folder.</param>
    /// <exception cref="FolderOfflineException">The folder is Offline.</exception>
    public static void ThrowIfOffline(IndexedFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        if (folder.Status == FolderStatus.Offline)
        {
            throw FolderOfflineException.For(folder.DisplayPath);
        }
    }

    /// <summary>
    /// Gets whether a failure means a directory on the file's path is gone, so the folder itself may be.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <returns><see langword="true"/> for a missing directory or drive.</returns>
    public static bool IsMissingDirectory(Exception exception) =>
        exception is DirectoryNotFoundException or DriveNotFoundException;

    /// <summary>
    /// After a directory wasn't found (<see cref="IsMissingDirectory"/>), tells an Offline folder from a deleted
    /// subfolder: when the folder's root can't be listed, the folder is marked Offline and the work waits; when the
    /// root is there but the item's file isn't, retrying won't fix it. Otherwise the missing directory wasn't the
    /// file's, and the caller rethrows the failure as it was.
    /// </summary>
    /// <param name="library">The open library.</param>
    /// <param name="folder">The item's folder.</param>
    /// <param name="file">The item's file.</param>
    /// <param name="files">Checks the folder's root and the file.</param>
    /// <param name="clock">The clock, for the status change.</param>
    /// <param name="failure">The failure.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the failure isn't about the item's folder or file.</returns>
    /// <exception cref="FolderOfflineException">The folder's root can't be listed.</exception>
    /// <exception cref="FileNotFoundException">The folder is there, but not the item's file.</exception>
    public static async Task ThrowForMissingDirectoryAsync(
        IOpenLibrary library,
        IndexedFolder folder,
        FileLocation file,
        IMediaFileSource files,
        TimeProvider clock,
        Exception failure,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(failure);

        if (!await files.IsAvailableAsync(folder.ToHandle(), cancellationToken).ConfigureAwait(false))
        {
            if (folder.Status != FolderStatus.Offline)
            {
                folder.MarkOffline(clock.GetUtcNow());
                await library.Folders.UpdateAvailabilityAsync(folder, cancellationToken).ConfigureAwait(false);
            }

            throw FolderOfflineException.For(folder.DisplayPath, failure);
        }

        if (await files.GetStatAsync(file, cancellationToken).ConfigureAwait(false) is null)
        {
            throw new FileNotFoundException($"The file's folder no longer exists: {failure.Message}", failure);
        }
    }
}
