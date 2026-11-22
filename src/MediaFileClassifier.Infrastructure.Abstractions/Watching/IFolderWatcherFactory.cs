using MediaFileClassifier.Infrastructure.Abstractions.Files;

namespace MediaFileClassifier.Infrastructure.Abstractions.Watching;

/// <summary>
/// Starts watching folders the user granted access to (E04-S01). On desktop it uses the file system's change
/// notifications; a platform that can't watch a folder throws from <see cref="Watch"/>.
/// </summary>
public interface IFolderWatcherFactory
{
    /// <summary>
    /// Starts watching a folder, recursively.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <returns>The running watcher. The caller disposes it.</returns>
    /// <exception cref="IOException">The folder can't be watched, e.g. it doesn't exist.</exception>
    /// <exception cref="ArgumentException">The platform can't watch this kind of folder.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder can't be read.</exception>
    IFolderWatcher Watch(FolderHandle folder);
}
