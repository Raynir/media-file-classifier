using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Watching;

namespace MediaFileClassifier.Infrastructure.Files;

/// <summary>
/// Creates <see cref="FileSystemFolderWatcher"/>s, for platforms where a granted folder is a plain path (desktop).
/// </summary>
public sealed class FileSystemFolderWatcherFactory : IFolderWatcherFactory
{
    /// <inheritdoc />
    public IFolderWatcher Watch(FolderHandle folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return new FileSystemFolderWatcher(folder);
    }
}
