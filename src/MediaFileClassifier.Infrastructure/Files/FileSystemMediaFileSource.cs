using System.IO.Enumeration;
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

    // Attributes of a file whose content isn't stored locally (cloud files API): reading it recalls it from the cloud.
    // .NET doesn't name the recall attributes, FILE_ATTRIBUTE_RECALL_ON_OPEN and FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS.
    // They're read from the directory listing, so the file is never opened. Other platforms never set them.
    private const FileAttributes CloudPlaceholderAttributes = FileAttributes.Offline | (FileAttributes)0x00040000 | (FileAttributes)0x00400000;

    private static readonly EnumerationOptions EnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };

    // One folder at a time, skipping nothing: ScanAsync filters hidden entries itself, and reports errors.
    private static readonly EnumerationOptions ScanOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
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
    public async IAsyncEnumerable<FolderScanEntry> ScanAsync(
        FolderHandle folder,
        FolderScanOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var root = LocalFolder.GetFullPath(folder, string.Empty);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"The folder '{root}' doesn't exist.");
        }

        var skipped = options.IncludeHidden ? 0 : FileAttributes.Hidden | FileAttributes.System;
        var pending = new Stack<string>();
        pending.Push(string.Empty);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            var fullPath = directory.Length == 0 ? root : LocalFolder.GetFullPath(folder, directory);
            var (entries, error) = ListFolder(fullPath);
            if (entries is null)
            {
                yield return FolderScanEntry.ForUnreadableFolder(directory, error!);
                continue;
            }

            var subfolders = new List<string>();
            foreach (var entry in entries)
            {
                if ((entry.Attributes & skipped) != 0)
                {
                    continue;
                }

                var relativePath = directory.Length == 0 ? entry.Name : directory + "/" + entry.Name;
                if (!entry.IsDirectory)
                {
                    yield return FolderScanEntry.ForFile(new MediaFileStat(relativePath, entry.Length, entry.LastModifiedUtc)
                    {
                        CreatedUtc = entry.CreatedUtc,
                        IsCloudPlaceholder = (entry.Attributes & CloudPlaceholderAttributes) != 0,
                    });
                }
                else if (IsLink(entry, Path.Combine(fullPath, entry.Name)))
                {
                    yield return FolderScanEntry.ForSkippedLink(relativePath);
                }
                else if (options.IncludeSubfolders && (options.ShouldEnterFolder?.Invoke(relativePath) ?? true))
                {
                    subfolders.Add(relativePath);
                }
            }

            yield return FolderScanEntry.ForFolder(directory);

            // Reversed, so subfolders are scanned in the order they were listed.
            for (var i = subfolders.Count - 1; i >= 0; i--)
            {
                pending.Push(subfolders[i]);
            }
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
        => new(LocalFolder.GetRelativePath(folder, file.FullName), file.Length, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero))
        {
            CreatedUtc = new DateTimeOffset(file.CreationTimeUtc, TimeSpan.Zero),
            IsCloudPlaceholder = (file.Attributes & CloudPlaceholderAttributes) != 0,
        };

    // Lists one folder completely, so a listing error is caught here rather than in the middle of yielding entries.
    // The folder may also have vanished since its parent was listed.
    private static (List<ScannedEntry>? Entries, string? Error) ListFolder(string fullPath)
    {
        try
        {
            return ([.. new FileSystemEnumerable<ScannedEntry>(fullPath, ToScannedEntry, ScanOptions)], null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (null, exception.Message);
        }
    }

    private static ScannedEntry ToScannedEntry(ref FileSystemEntry entry) => new(
        entry.FileName.ToString(),
        entry.IsDirectory,
        entry.Attributes,
        entry.IsDirectory ? 0 : entry.Length,
        entry.CreationTimeUtc,
        entry.LastWriteTimeUtc);

    // Cloud-sync folders such as OneDrive are reparse points too, so only real links (symbolic links and junctions,
    // which have a target) are skipped. A link that can't be read is skipped as well.
    private static bool IsLink(ScannedEntry entry, string fullPath)
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) == 0)
        {
            return false;
        }

        try
        {
            return new DirectoryInfo(fullPath).LinkTarget is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private sealed record ScannedEntry(
        string Name,
        bool IsDirectory,
        FileAttributes Attributes,
        long Length,
        DateTimeOffset CreatedUtc,
        DateTimeOffset LastModifiedUtc);
}
