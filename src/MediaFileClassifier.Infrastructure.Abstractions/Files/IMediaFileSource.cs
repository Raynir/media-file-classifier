namespace MediaFileClassifier.Infrastructure.Abstractions.Files;

/// <summary>
/// Reads files in a folder the user granted access to (E01-S07-T02). Ingestion and hashing go through this,
/// never through file-system paths, because Android hands out content URIs instead (E20).
/// </summary>
public interface IMediaFileSource
{
    /// <summary>
    /// Lists the files under a folder, recursively. Hidden and system files, and subfolders the app can't
    /// read, are skipped.
    /// </summary>
    /// <param name="folder">Granted folder.</param>
    /// <param name="relativeDirectory">Subfolder to start from, with <c>/</c> separators. Empty for the whole folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The files, with paths relative to <paramref name="folder"/>.</returns>
    IAsyncEnumerable<MediaFileStat> EnumerateFilesAsync(
        FolderHandle folder,
        string relativeDirectory = "",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans a folder for file discovery (E03-S02). It lists one folder at a time, so memory stays flat on large trees.
    /// Folders that can't be listed are reported and skipped, and links to folders are reported and never followed.
    /// </summary>
    /// <param name="folder">Granted folder.</param>
    /// <param name="options">What to list.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The files, with paths relative to <paramref name="folder"/>, and an entry for each folder listed or skipped.</returns>
    /// <exception cref="DirectoryNotFoundException">The folder doesn't exist.</exception>
    IAsyncEnumerable<FolderScanEntry> ScanAsync(
        FolderHandle folder,
        FolderScanOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a file for reading. The caller disposes the stream.
    /// </summary>
    /// <param name="file">File to open.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only stream.</returns>
    /// <exception cref="FileNotFoundException">The file doesn't exist.</exception>
    Task<Stream> OpenReadAsync(FileLocation file, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the size and modification time of a file.
    /// </summary>
    /// <param name="file">File to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The file's stats, or <see langword="null"/> when it doesn't exist.</returns>
    Task<MediaFileStat?> GetStatAsync(FileLocation file, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a folder can be reached now: it exists and can be listed (E02-S03-T05). A folder that can't,
    /// e.g. on an unplugged drive, is offline.
    /// </summary>
    /// <param name="folder">Granted folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the folder can be listed.</returns>
    Task<bool> IsAvailableAsync(FolderHandle folder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the label of the volume a folder is on, e.g. <c>Backup-2TB</c>, so an Offline folder can name the drive
    /// to connect (E14-S07-T02). Platforms without volume labels, e.g. Android's granted folders, return
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="folder">Granted folder, which should be reachable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The label, or <see langword="null"/> when the volume has none or it can't be read.</returns>
    Task<string?> GetVolumeLabelAsync(FolderHandle folder, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

    /// <summary>
    /// Gets where a folder is inside another one, to find overlapping indexed folders (E02-S03-T02). Only the handles
    /// are compared; nothing is read.
    /// </summary>
    /// <param name="folder">The folder that may be inside <paramref name="parent"/>.</param>
    /// <param name="parent">The folder that may contain it.</param>
    /// <returns>
    /// The path of <paramref name="folder"/> relative to <paramref name="parent"/>, with <c>/</c> separators; empty
    /// when they're the same folder; <see langword="null"/> when it isn't inside, or the platform can't tell.
    /// </returns>
    string? GetPathInside(FolderHandle folder, FolderHandle parent);
}
