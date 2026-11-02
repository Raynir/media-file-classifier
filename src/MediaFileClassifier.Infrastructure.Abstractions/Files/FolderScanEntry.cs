namespace MediaFileClassifier.Infrastructure.Abstractions.Files;

/// <summary>
/// One result of <see cref="IMediaFileSource.ScanAsync"/>: a file, a folder that was listed, or a folder that was skipped.
/// </summary>
/// <param name="Kind">What the entry reports.</param>
/// <param name="RelativePath">Path inside the scanned folder with <c>/</c> separators, or an empty string for the folder itself.</param>
public sealed record FolderScanEntry(FolderScanEntryKind Kind, string RelativePath)
{
    /// <summary>
    /// Gets the file's stats, for a <see cref="FolderScanEntryKind.File"/> entry.
    /// </summary>
    public MediaFileStat? File { get; init; }

    /// <summary>
    /// Gets why the folder couldn't be listed, for a <see cref="FolderScanEntryKind.UnreadableFolder"/> entry.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Creates the entry of a file.
    /// </summary>
    /// <param name="file">The file's stats.</param>
    /// <returns>The entry.</returns>
    public static FolderScanEntry ForFile(MediaFileStat file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return new(FolderScanEntryKind.File, file.RelativePath) { File = file };
    }

    /// <summary>
    /// Creates the entry of a folder that was listed.
    /// </summary>
    /// <param name="relativePath">The folder's relative path.</param>
    /// <returns>The entry.</returns>
    public static FolderScanEntry ForFolder(string relativePath) => new(FolderScanEntryKind.Folder, relativePath);

    /// <summary>
    /// Creates the entry of a folder that couldn't be listed.
    /// </summary>
    /// <param name="relativePath">The folder's relative path.</param>
    /// <param name="error">Why it couldn't be listed.</param>
    /// <returns>The entry.</returns>
    public static FolderScanEntry ForUnreadableFolder(string relativePath, string error) =>
        new(FolderScanEntryKind.UnreadableFolder, relativePath) { Error = error };

    /// <summary>
    /// Creates the entry of a skipped folder link.
    /// </summary>
    /// <param name="relativePath">The link's relative path.</param>
    /// <returns>The entry.</returns>
    public static FolderScanEntry ForSkippedLink(string relativePath) => new(FolderScanEntryKind.SkippedLink, relativePath);
}
