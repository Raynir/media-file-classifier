namespace MediaFileClassifier.Infrastructure.Abstractions.Discovery;

/// <summary>
/// The summary of a discovery scan (E03-S02).
/// </summary>
public sealed record DiscoveryResult
{
    /// <summary>
    /// Gets the number of folders listed.
    /// </summary>
    public int FoldersScanned { get; init; }

    /// <summary>
    /// Gets the number of supported media files found, new or already in the library.
    /// </summary>
    public int FilesFound { get; init; }

    /// <summary>
    /// Gets the number of new items registered with status Discovered.
    /// </summary>
    public int ItemsRegistered { get; init; }

    /// <summary>
    /// Gets the number of files with a supported extension whose content isn't a supported format.
    /// </summary>
    public int Unsupported { get; init; }

    /// <summary>
    /// Gets the number of files that couldn't be read, e.g. because another program locks them.
    /// </summary>
    public int UnreadableFiles { get; init; }

    /// <summary>
    /// Gets the folders that couldn't be listed, e.g. because permission was denied, with paths relative to the folder root.
    /// </summary>
    public IReadOnlyList<string> UnreadableFolders { get; init; } = [];

    /// <summary>
    /// Gets the links to folders that were skipped, with paths relative to the folder root.
    /// </summary>
    public IReadOnlyList<string> SkippedLinks { get; init; } = [];
}
