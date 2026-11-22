using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Watching;

namespace MediaFileClassifier.Infrastructure.Files;

/// <summary>
/// Watches a local folder with <see cref="FileSystemWatcher"/> (E04-S01-T01), for platforms where a granted folder is a
/// plain path (desktop). Its buffer is the largest the platform allows, so overflows are rare; when one happens anyway,
/// or the watcher fails, <see cref="Failed"/> tells so the folder can be rescanned (E04-S01-T04).
/// </summary>
internal sealed class FileSystemFolderWatcher : IFolderWatcher
{
    /// <summary>
    /// The buffer size, 64 KB: the most Windows allows for a network share, and room for a few thousand events.
    /// </summary>
    public const int BufferSize = 64 * 1024;

    private readonly FileSystemWatcher watcher;
    private readonly string root;
    private volatile bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemFolderWatcher"/> class and starts watching.
    /// </summary>
    /// <param name="folder">The folder; its ID is the full path.</param>
    /// <exception cref="IOException">The folder doesn't exist or can't be watched.</exception>
    public FileSystemFolderWatcher(FolderHandle folder)
    {
        root = LocalFolder.GetFullPath(folder, string.Empty);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"'{folder.DisplayName}' doesn't exist.");
        }

        watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            InternalBufferSize = BufferSize,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        watcher.Created += (_, e) => Raise(FolderWatchEventKind.Created, e.FullPath, null);
        watcher.Changed += (_, e) => Raise(FolderWatchEventKind.Changed, e.FullPath, null);
        watcher.Deleted += (_, e) => Raise(FolderWatchEventKind.Deleted, e.FullPath, null);
        watcher.Renamed += (_, e) => Raise(FolderWatchEventKind.Renamed, e.FullPath, e.OldFullPath);
        watcher.Error += OnError;
        try
        {
            watcher.EnableRaisingEvents = true;
        }
        catch
        {
            watcher.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public event EventHandler<FolderWatchEvent>? Changed;

    /// <inheritdoc />
    public event EventHandler<FolderWatchErrorEventArgs>? Failed;

    /// <inheritdoc />
    public void Dispose()
    {
        disposed = true;
        watcher.Dispose();
    }

    private void Raise(FolderWatchEventKind kind, string fullPath, string? oldFullPath)
    {
        if (disposed)
        {
            return;
        }

        var relative = ToRelative(fullPath);
        if (relative.Length == 0)
        {
            // The folder itself; its going away shows up as an error.
            return;
        }

        var isDirectory = kind != FolderWatchEventKind.Deleted && Directory.Exists(fullPath);
        Changed?.Invoke(this, new FolderWatchEvent(kind, relative, oldFullPath is null ? null : ToRelative(oldFullPath), isDirectory));
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        if (disposed)
        {
            return;
        }

        var exception = e.GetException();
        Failed?.Invoke(this, new FolderWatchErrorEventArgs(exception is InternalBufferOverflowException, exception));
    }

    private string ToRelative(string fullPath) =>
        Path.GetRelativePath(root, fullPath) is var relative && relative != "." ? relative.Replace(Path.DirectorySeparatorChar, '/') : string.Empty;
}
