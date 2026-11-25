namespace MediaFileClassifier.Domain.Libraries;

/// <summary>
/// How an indexed folder is kept up to date (E04-S05-T01). Stored by number, so the values never change.
/// </summary>
public enum MonitoringMode
{
    /// <summary>
    /// Picked from where the folder is: <see cref="Poll"/> for a network path (a UNC path or a mapped network drive),
    /// <see cref="Watch"/> otherwise. The default.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// A live file-system watcher reports changes as they happen (E04-S01).
    /// </summary>
    Watch = 1,

    /// <summary>
    /// A quick sync (E04-S03) runs at the poll interval. For shares that don't report changes reliably.
    /// </summary>
    Poll = 2,

    /// <summary>
    /// Nothing runs on its own, not even the quick syncs on library open and drive reconnect: only a rescan the user
    /// starts.
    /// </summary>
    Manual = 3,
}
