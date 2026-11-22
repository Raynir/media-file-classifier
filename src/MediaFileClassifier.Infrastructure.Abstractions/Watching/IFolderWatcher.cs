namespace MediaFileClassifier.Infrastructure.Abstractions.Watching;

/// <summary>
/// Watches one folder and everything under it for changes (E04-S01). Disposing it stops the events, though one
/// already being raised can still arrive.
/// </summary>
public interface IFolderWatcher : IDisposable
{
    /// <summary>
    /// Raised for each raw change, on a thread-pool thread. A single save can raise several.
    /// </summary>
    event EventHandler<FolderWatchEvent>? Changed;

    /// <summary>
    /// Raised when events were lost, on a thread-pool thread (E04-S01-T04).
    /// </summary>
    event EventHandler<FolderWatchErrorEventArgs>? Failed;
}
