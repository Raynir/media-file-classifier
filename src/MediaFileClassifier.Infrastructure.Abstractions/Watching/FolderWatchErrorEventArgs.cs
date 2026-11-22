namespace MediaFileClassifier.Infrastructure.Abstractions.Watching;

/// <summary>
/// Tells that an <see cref="IFolderWatcher"/> lost events (E04-S01-T04): its buffer overflowed, or it failed, e.g.
/// because the folder went away or a network share dropped.
/// </summary>
/// <param name="isOverflow">Whether the buffer overflowed; the watcher still runs.</param>
/// <param name="exception">The error, if there was one.</param>
public sealed class FolderWatchErrorEventArgs(bool isOverflow, Exception? exception) : EventArgs
{
    /// <summary>
    /// Gets a value indicating whether the buffer overflowed. The watcher still runs, but events were lost. Otherwise the
    /// watcher failed and may have stopped.
    /// </summary>
    public bool IsOverflow { get; } = isOverflow;

    /// <summary>
    /// Gets the error, if there was one.
    /// </summary>
    public Exception? Exception { get; } = exception;
}
