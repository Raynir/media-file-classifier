namespace MediaFileClassifier.UseCases.Monitoring;

/// <summary>
/// What a stable change of a watched folder is (E04-S01-T02).
/// </summary>
public enum FolderChangeKind
{
    /// <summary>
    /// The file is new or its content changed, and it's stable and readable now. The watcher can't tell new from
    /// changed: a copy raises both, so the library decides.
    /// </summary>
    Changed,

    /// <summary>
    /// The path is gone. It may have been a folder, in which case everything under it is gone.
    /// </summary>
    Deleted,

    /// <summary>
    /// The file moved inside the folder from <see cref="FolderChange.OldRelativePath"/>, and it's stable and readable.
    /// A renamed folder is reported as a rename of each file in it.
    /// </summary>
    Renamed,
}
