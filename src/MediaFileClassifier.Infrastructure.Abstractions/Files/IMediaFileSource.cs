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
}
