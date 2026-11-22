using System.Runtime.CompilerServices;
using MediaFileClassifier.Infrastructure.Abstractions.Files;

namespace MediaFileClassifier.Testing.Files;

/// <summary>
/// An <see cref="IMediaFileSource"/> over files kept in memory, by path relative to their folder. A file can be locked,
/// as one still being written is, and then fails to open. Folders are left to the test: every folder is available.
/// </summary>
public sealed class InMemoryMediaFileSource : IMediaFileSource
{
    private readonly Dictionary<string, MediaFileStat> files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> locked = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> broken = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock syncRoot = new();

    /// <summary>
    /// Gets how many times a file was opened for reading.
    /// </summary>
    public int Opens { get; private set; }

    /// <summary>
    /// Writes a file, or changes its size and modification time.
    /// </summary>
    /// <param name="relativePath">The path, with <c>/</c> separators.</param>
    /// <param name="length">Its size.</param>
    /// <param name="modified">Its modification time.</param>
    /// <param name="isHidden">Whether it's hidden.</param>
    /// <param name="isCloudPlaceholder">Whether it's a cloud placeholder.</param>
    public void Write(string relativePath, long length, DateTimeOffset modified, bool isHidden = false, bool isCloudPlaceholder = false)
    {
        lock (syncRoot)
        {
            files[relativePath] = new MediaFileStat(relativePath, length, modified) { IsHidden = isHidden, IsCloudPlaceholder = isCloudPlaceholder };
        }
    }

    /// <summary>
    /// Deletes a file.
    /// </summary>
    /// <param name="relativePath">The path.</param>
    public void Delete(string relativePath)
    {
        lock (syncRoot)
        {
            files.Remove(relativePath);
        }
    }

    /// <summary>
    /// Makes reading a file's size and times fail with an unexpected error, not an I/O one.
    /// </summary>
    /// <param name="relativePath">The path.</param>
    public void Break(string relativePath)
    {
        lock (syncRoot)
        {
            broken.Add(relativePath);
        }
    }

    /// <summary>
    /// Locks or unlocks a file, so opening it fails or works.
    /// </summary>
    /// <param name="relativePath">The path.</param>
    /// <param name="isLocked">Whether it's locked.</param>
    public void Lock(string relativePath, bool isLocked = true)
    {
        lock (syncRoot)
        {
            if (isLocked)
            {
                locked.Add(relativePath);
            }
            else
            {
                locked.Remove(relativePath);
            }
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<MediaFileStat> EnumerateFilesAsync(FolderHandle folder, string relativeDirectory = "", [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<MediaFileStat> found;
        lock (syncRoot)
        {
            var prefix = relativeDirectory.Length == 0 ? string.Empty : relativeDirectory.TrimEnd('/') + "/";
            found = [.. files.Values.Where(file => file.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).OrderBy(file => file.RelativePath, StringComparer.Ordinal)];
        }

        foreach (var file in found)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return file;
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<FolderScanEntry> ScanAsync(FolderHandle folder, FolderScanOptions options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Scanning isn't faked.");

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(FileLocation file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        lock (syncRoot)
        {
            Opens++;
            if (locked.Contains(file.RelativePath))
            {
                return Task.FromException<Stream>(new IOException("The file is being used by another process."));
            }

            return files.ContainsKey(file.RelativePath)
                ? Task.FromResult<Stream>(new MemoryStream())
                : Task.FromException<Stream>(new FileNotFoundException(file.RelativePath));
        }
    }

    /// <inheritdoc />
    public Task<MediaFileStat?> GetStatAsync(FileLocation file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        lock (syncRoot)
        {
            return broken.Contains(file.RelativePath)
                ? Task.FromException<MediaFileStat?>(new InvalidOperationException($"'{file.RelativePath}' can't be read."))
                : Task.FromResult(files.GetValueOrDefault(file.RelativePath));
        }
    }

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(FolderHandle folder, CancellationToken cancellationToken = default) => Task.FromResult(true);

    /// <inheritdoc />
    public string? GetPathInside(FolderHandle folder, FolderHandle parent) => null;
}
