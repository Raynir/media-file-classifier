namespace MediaFileClassifier.Domain.Libraries;

/// <summary>
/// Whether an indexed folder can currently be reached.
/// </summary>
public enum FolderStatus
{
    /// <summary>
    /// The folder is reachable and is scanned and monitored.
    /// </summary>
    Online,

    /// <summary>
    /// The folder can't be reached, e.g. a removable drive is unplugged or a permission was revoked.
    /// Its items stay in the library and keep their metadata.
    /// </summary>
    Offline,
}
