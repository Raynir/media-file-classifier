namespace MediaFileClassifier.Infrastructure.Abstractions.Watching;

/// <summary>
/// What a raw file-system event reported (E04-S01).
/// </summary>
public enum FolderWatchEventKind
{
    /// <summary>A file or folder was created.</summary>
    Created,

    /// <summary>A file's content, size or modification time changed.</summary>
    Changed,

    /// <summary>A file or folder was deleted. The platform can't tell which, since it's gone.</summary>
    Deleted,

    /// <summary>A file or folder was renamed or moved inside the watched folder.</summary>
    Renamed,
}
