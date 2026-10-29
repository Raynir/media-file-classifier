using System.Runtime.CompilerServices;
using MediaFileClassifier.Infrastructure.Abstractions.Files;

namespace MediaFileClassifier.Infrastructure.Files;

/// <summary>
/// Reads granted folders as plain file-system paths. The desktop head registers it; Android brings
/// a Storage Access Framework implementation (E20).
/// </summary>
public sealed class FileSystemMediaFileSource : IMediaFileSource
{
    private const int BufferSize = 81920;

    private static readonly EnumerationOptions EnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };

    /// <inheritdoc />
    public async IAsyncEnumerable<MediaFileStat> EnumerateFilesAsync(
        FolderHandle folder,
        string relativeDirectory = "",
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var directory = new DirectoryInfo(LocalFolder.GetFullPath(folder, relativeDirectory));
        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException($"The folder '{directory.FullName}' doesn't exist.");
        }

        foreach (var file in directory.EnumerateFiles("*", EnumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return ToStat(folder, file);
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(FileLocation file, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(
            LocalFolder.GetFullPath(file),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public Task<MediaFileStat?> GetStatAsync(FileLocation file, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = new FileInfo(LocalFolder.GetFullPath(file));
        return Task.FromResult(info.Exists ? ToStat(file.Folder, info) : null);
    }

    private static MediaFileStat ToStat(FolderHandle folder, FileInfo file)
        => new(LocalFolder.GetRelativePath(folder, file.FullName), file.Length, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero));
}
