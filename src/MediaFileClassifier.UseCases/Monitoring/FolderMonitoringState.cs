namespace MediaFileClassifier.UseCases.Monitoring;

/// <summary>
/// How an Online folder is monitored now (E04-S06). A folder that's Offline is shown as Offline, whatever its mode.
/// </summary>
public enum FolderMonitoringState
{
    /// <summary>
    /// The folder isn't monitored: monitoring is turned off for the library, or the platform can't watch folders.
    /// </summary>
    Off,

    /// <summary>
    /// A watcher runs and changes are imported as they appear.
    /// </summary>
    Watching,

    /// <summary>
    /// A watcher runs, but monitoring is paused, so changes wait.
    /// </summary>
    Paused,

    /// <summary>
    /// The folder is Online but isn't watched: its watcher failed or couldn't start. It's tried again by itself.
    /// </summary>
    Error,

    /// <summary>
    /// The folder runs in Poll mode (E04-S05): a quick sync runs at the poll interval.
    /// </summary>
    Polling,

    /// <summary>
    /// The folder is in Manual mode (E04-S05): nothing runs on its own.
    /// </summary>
    Manual,

    /// <summary>
    /// The folder can't be reached; it's caught up when it's back. <see cref="FolderWatchManager.GetState"/> doesn't
    /// return it: the folder list shows Offline from the folder's status.
    /// </summary>
    Offline,
}
