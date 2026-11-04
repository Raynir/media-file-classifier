using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;

namespace MediaFileClassifier.UseCases.Libraries;

/// <summary>
/// Helpers shared by the indexed-folder use cases (E02-S03).
/// </summary>
public static class IndexedFolders
{
    /// <summary>
    /// Job type that removes a folder from the library (E02-S03-T04).
    /// </summary>
    public const string RemoveJob = "folder-remove";

    /// <summary>
    /// The stage of <see cref="RemoveJob"/>: deletes the folder's items in batches, then the folder.
    /// </summary>
    public const string RemoveStage = "folder.remove";

    /// <summary>
    /// The status reason of an item excluded by its folder's rules (E02-S03-T03), which tells it apart from an item
    /// the user excluded (D024).
    /// </summary>
    public const string ExcludedByRules = "Excluded by the folder's rules.";

    /// <summary>
    /// Checks whether an item its folder's rules excluded is included by them again, so it's imported again.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="rules">Its folder's rules.</param>
    /// <returns><see langword="true"/> for an item Excluded by the rules that they now include.</returns>
    public static bool IsBackInRules(MediaItem item, FolderRuleMatcher rules)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(rules);

        return item.Status == MediaItemStatus.Excluded && item.StatusReason == ExcludedByRules && rules.IsIncluded(item.Path.Value);
    }

    /// <summary>
    /// Gets the handle the platform reads a folder through.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <returns>The handle.</returns>
    public static FolderHandle ToHandle(this IndexedFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return new FolderHandle(folder.PlatformHandle, folder.DisplayPath);
    }

    /// <summary>
    /// Gets the folders of a library that aren't being removed, in the order they were added.
    /// </summary>
    /// <param name="library">The open library.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The folders.</returns>
    public static async Task<IReadOnlyList<IndexedFolder>> GetActiveAsync(IOpenLibrary library, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(library);

        var removing = (await library.Jobs.GetUnfinishedJobsAsync(cancellationToken).ConfigureAwait(false))
            .Where(job => job.Type == RemoveJob && job.FolderId is not null)
            .Select(job => job.FolderId!.Value)
            .ToHashSet();
        return [.. (await library.Folders.GetAllAsync(cancellationToken).ConfigureAwait(false)).Where(folder => !removing.Contains(folder.Id))];
    }

    /// <summary>
    /// Finds how a folder overlaps the indexed folders (E02-S03-T02).
    /// </summary>
    /// <param name="files">Compares folder handles.</param>
    /// <param name="folder">The folder the user picked.</param>
    /// <param name="indexed">The indexed folders.</param>
    /// <returns>The overlap.</returns>
    public static FolderOverlap FindOverlap(IMediaFileSource files, FolderHandle folder, IReadOnlyList<IndexedFolder> indexed)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(indexed);

        var covering = indexed
            .Select(existing => (Folder: existing, Path: files.GetPathInside(folder, existing.ToHandle())))
            .Where(match => match.Path is not null)
            .ToList();
        if (covering.Find(match => match.Path!.Length == 0).Folder is { } same)
        {
            return new FolderOverlap(FolderOverlapKind.AlreadyIndexed, [same]);
        }

        if (covering.Count > 0)
        {
            return new FolderOverlap(FolderOverlapKind.InsideIndexed, [covering[0].Folder]);
        }

        List<IndexedFolder> inside = [.. indexed.Where(existing => files.GetPathInside(existing.ToHandle(), folder) is { Length: > 0 })];
        return inside.Count > 0 ? new FolderOverlap(FolderOverlapKind.ContainsIndexed, inside) : FolderOverlap.None;
    }
}
