namespace MediaFileClassifier.Infrastructure.Abstractions.Files;

/// <summary>
/// What a <see cref="FolderScanEntry"/> reports.
/// </summary>
public enum FolderScanEntryKind
{
    /// <summary>
    /// A file was found.
    /// </summary>
    File,

    /// <summary>
    /// A folder was listed completely. It comes after the folder's files.
    /// </summary>
    Folder,

    /// <summary>
    /// A folder couldn't be listed, e.g. permission was denied. The scan goes on without it.
    /// </summary>
    UnreadableFolder,

    /// <summary>
    /// A symbolic link or junction to a folder was skipped. Links are never followed, so they can't loop.
    /// </summary>
    SkippedLink,
}
