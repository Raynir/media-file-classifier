using System.Runtime.CompilerServices;
using Android.Provider;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using AndroidContentResolver = Android.Content.ContentResolver;
using AndroidUri = Android.Net.Uri;

namespace MediaFileClassifier.App.Android.Platform;

/// <summary>
/// Reads a folder granted through the Storage Access Framework (E01-S07-T02). <see cref="FolderHandle.Id"/> is the
/// tree URI. Files are listed by querying <see cref="DocumentsContract"/> directly, one query per folder, because
/// <c>DocumentFile</c> runs a query per file and property.
/// </summary>
/// <param name="contentResolver">The app's content resolver.</param>
internal sealed class SafMediaFileSource(AndroidContentResolver contentResolver) : IMediaFileSource
{
    private static readonly string[] Columns =
    [
        DocumentsContract.Document.ColumnDocumentId,
        DocumentsContract.Document.ColumnDisplayName,
        DocumentsContract.Document.ColumnMimeType,
        DocumentsContract.Document.ColumnSize,
        DocumentsContract.Document.ColumnLastModified,
    ];

    /// <inheritdoc />
    public async IAsyncEnumerable<MediaFileStat> EnumerateFilesAsync(
        FolderHandle folder,
        string relativeDirectory = "",
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var treeUri = AndroidUri.Parse(folder.Id)!;
        var start = await Task.Run(() => Resolve(treeUri, relativeDirectory, cancellationToken), cancellationToken);
        if (start is not { IsDirectory: true })
        {
            yield break;
        }

        var pending = new Stack<(string DocumentId, string RelativePath)>();
        pending.Push((start.DocumentId, relativeDirectory.Trim('/')));
        while (pending.Count > 0)
        {
            var (documentId, path) = pending.Pop();
            var children = await Task.Run(() => QueryChildren(treeUri, documentId, cancellationToken), cancellationToken);
            foreach (var child in children.Where(child => !child.Name.StartsWith('.')))
            {
                var childPath = path.Length == 0 ? child.Name : path + "/" + child.Name;
                if (child.IsDirectory)
                {
                    pending.Push((child.DocumentId, childPath));
                }
                else
                {
                    yield return new MediaFileStat(childPath, child.Length, child.LastModifiedUtc);
                }
            }
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<FolderScanEntry> ScanAsync(
        FolderHandle folder,
        FolderScanOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var treeUri = AndroidUri.Parse(folder.Id)!;
        var pending = new Stack<(string DocumentId, string RelativePath)>();
        pending.Push((DocumentsContract.GetTreeDocumentId(treeUri)!, string.Empty));
        while (pending.Count > 0)
        {
            var (documentId, path) = pending.Pop();
            var (children, error) = await Task.Run(() => TryQueryChildren(treeUri, documentId, cancellationToken), cancellationToken);
            if (children is null && path.Length == 0)
            {
                // The grant was revoked or the folder deleted: the folder is offline, as on desktop.
                throw new DirectoryNotFoundException($"The folder '{folder.DisplayName}' can't be read: {error}");
            }

            if (children is null)
            {
                yield return FolderScanEntry.ForUnreadableFolder(path, error!);
                continue;
            }

            var subfolders = new List<(string DocumentId, string RelativePath)>();
            foreach (var child in children.Where(child => options.IncludeHidden || !child.Name.StartsWith('.')))
            {
                var childPath = path.Length == 0 ? child.Name : path + "/" + child.Name;
                if (!child.IsDirectory)
                {
                    yield return FolderScanEntry.ForFile(new MediaFileStat(childPath, child.Length, child.LastModifiedUtc));
                }
                else if (options.IncludeSubfolders && (options.ShouldEnterFolder?.Invoke(childPath) ?? true))
                {
                    subfolders.Add((child.DocumentId, childPath));
                }
            }

            yield return FolderScanEntry.ForFolder(path);
            for (var i = subfolders.Count - 1; i >= 0; i--)
            {
                pending.Push(subfolders[i]);
            }
        }
    }

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(FileLocation file, CancellationToken cancellationToken = default)
        => Task.Run(
            () =>
            {
                var treeUri = AndroidUri.Parse(file.Folder.Id)!;
                var document = Resolve(treeUri, file.RelativePath, cancellationToken);
                if (document is null or { IsDirectory: true })
                {
                    throw new FileNotFoundException("The file doesn't exist.", file.RelativePath);
                }

                var uri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, document.DocumentId)!;
                return contentResolver.OpenInputStream(uri)
                    ?? throw new FileNotFoundException("The file can't be opened.", file.RelativePath);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<MediaFileStat?> GetStatAsync(FileLocation file, CancellationToken cancellationToken = default)
        => Task.Run(
            () =>
            {
                var document = Resolve(AndroidUri.Parse(file.Folder.Id)!, file.RelativePath, cancellationToken);
                return document is null or { IsDirectory: true }
                    ? null
                    : new MediaFileStat(file.RelativePath, document.Length, document.LastModifiedUtc);
            },
            cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// SAF has no lookup by path, so a relative path is resolved one segment at a time: one query per folder level.
    /// </remarks>
    public async Task<bool> IsAvailableAsync(FolderHandle folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);

        var treeUri = AndroidUri.Parse(folder.Id)!;
        var (children, _) = await Task.Run(() => TryQueryChildren(treeUri, DocumentsContract.GetTreeDocumentId(treeUri)!, cancellationToken), cancellationToken);
        return children is not null;
    }

    /// <inheritdoc />
    /// <remarks>Tree URIs of different grants aren't compared yet, so overlapping folders aren't found (E20).</remarks>
    public string? GetPathInside(FolderHandle folder, FolderHandle parent) => folder.Id == parent.Id ? string.Empty : null;

    private Document? Resolve(AndroidUri treeUri, string relativePath, CancellationToken cancellationToken)
    {
        Document? current = new(DocumentsContract.GetTreeDocumentId(treeUri)!, string.Empty, true, 0, DateTimeOffset.UnixEpoch);
        foreach (var segment in relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = QueryChildren(treeUri, current.DocumentId, cancellationToken)
                .FirstOrDefault(child => string.Equals(child.Name, segment, StringComparison.Ordinal));
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    // SAF has no links, but a provider can refuse a folder, e.g. when the grant was revoked.
    private (List<Document>? Children, string? Error) TryQueryChildren(AndroidUri treeUri, string documentId, CancellationToken cancellationToken)
    {
        try
        {
            return (QueryChildren(treeUri, documentId, cancellationToken), null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (null, exception.Message);
        }
    }

    private List<Document> QueryChildren(AndroidUri treeUri, string documentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, documentId)!;
        using var cursor = contentResolver.Query(childrenUri, Columns, null, null, null);
        var children = new List<Document>();
        while (cursor?.MoveToNext() == true)
        {
            children.Add(new Document(
                cursor.GetString(0)!,
                cursor.GetString(1) ?? string.Empty,
                cursor.GetString(2) == DocumentsContract.Document.MimeTypeDir,
                cursor.IsNull(3) ? 0 : cursor.GetLong(3),
                DateTimeOffset.FromUnixTimeMilliseconds(cursor.IsNull(4) ? 0 : cursor.GetLong(4))));
        }

        return children;
    }

    private sealed record Document(string DocumentId, string Name, bool IsDirectory, long Length, DateTimeOffset LastModifiedUtc);
}
