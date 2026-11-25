namespace MediaFileClassifier.Infrastructure.Abstractions.Watching;

/// <summary>
/// A raw event from an <see cref="IFolderWatcher"/>, before buffering (E04-S01). Paths are relative to the watched
/// folder, with <c>/</c> separators.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="RelativePath">The path of the file or folder; for a rename, its new path.</param>
/// <param name="OldRelativePath">For a rename, the old path; otherwise <see langword="null"/>.</param>
/// <param name="IsDirectory">
/// Whether the path was a folder when the event was seen. Always <see langword="false"/> for a deletion, which leaves
/// nothing to look at.
/// </param>
public sealed record FolderWatchEvent(FolderWatchEventKind Kind, string RelativePath, string? OldRelativePath = null, bool IsDirectory = false)
{
    /// <summary>
    /// Gets a value indicating whether a renamed path was already gone when the event was seen, e.g. renamed again at
    /// once, so it may have been a folder (MFC-1032). <see cref="IsDirectory"/> is <see langword="false"/> then.
    /// </summary>
    public bool IsGone { get; init; }
}
