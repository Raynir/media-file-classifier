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
    /// Gets the number of supported media files found, new or already in the library. A file already in the library
    /// counts without its content being read again.
    /// </summary>
    public int FilesFound { get; init; }

    /// <summary>
    /// Gets the number of new items registered with status Discovered.
    /// </summary>
    public int ItemsRegistered { get; init; }

    /// <summary>
    /// Gets the number of files already in the library with the same size and modification time. They were skipped
    /// without being read.
    /// </summary>
    public int Unchanged { get; init; }

    /// <summary>
    /// Gets the number of files already in the library whose size or modification time differs. They were skipped
    /// without being read; updating them is the rescan's job (E02-S04).
    /// </summary>
    public int Changed { get; init; }

    /// <summary>
    /// Gets the number of new cloud placeholders that were skipped without being read, because the folder doesn't
    /// include them.
    /// </summary>
    public int CloudPlaceholdersSkipped { get; init; }

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

    /// <summary>
    /// Gets the tracked files of a partial folder (D012) that couldn't be looked up, with paths relative to the folder
    /// root. Like the files in an unreadable folder, they can't be checked, so they aren't missing.
    /// </summary>
    public IReadOnlyList<string> UncheckedFiles { get; init; } = [];
}
