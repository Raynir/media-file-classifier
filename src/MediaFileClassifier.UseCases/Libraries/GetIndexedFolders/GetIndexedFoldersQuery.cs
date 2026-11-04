using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Import;
using MediatR;

namespace MediaFileClassifier.UseCases.Libraries.GetIndexedFolders;

/// <summary>
/// Lists the open library's folders with their state and item counts, for the folders view (E02-S03-T06). Folders
/// being removed aren't listed.
/// </summary>
public sealed record GetIndexedFoldersQuery : IRequest<IReadOnlyList<IndexedFolderSummary>>;

/// <summary>
/// What the folders view shows of a folder.
/// </summary>
public enum IndexedFolderState
{
    /// <summary>
    /// Reachable, and not being imported.
    /// </summary>
    Online,

    /// <summary>
    /// Not reachable, e.g. its drive is unplugged. Its items are kept.
    /// </summary>
    Offline,

    /// <summary>
    /// Being imported.
    /// </summary>
    Scanning,
}

/// <summary>
/// A folder with its state and item counts.
/// </summary>
/// <param name="Folder">The folder.</param>
/// <param name="State">Its state.</param>
/// <param name="ItemCount">Its items, except the excluded ones.</param>
/// <param name="ExcludedCount">Its items excluded by its rules.</param>
public sealed record IndexedFolderSummary(IndexedFolder Folder, IndexedFolderState State, int ItemCount, int ExcludedCount);

/// <summary>
/// Handles <see cref="GetIndexedFoldersQuery"/>. Without a library open the list is empty.
/// </summary>
/// <param name="session">Holds the open library.</param>
public sealed class GetIndexedFoldersQueryHandler(ILibrarySession session) : IRequestHandler<GetIndexedFoldersQuery, IReadOnlyList<IndexedFolderSummary>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<IndexedFolderSummary>> Handle(GetIndexedFoldersQuery request, CancellationToken cancellationToken)
    {
        if (session.Current is not { } library)
        {
            return [];
        }

        var folders = await IndexedFolders.GetActiveAsync(library, cancellationToken).ConfigureAwait(false);
        var importing = (await library.Jobs.GetUnfinishedJobsAsync(cancellationToken).ConfigureAwait(false))
            .Where(job => job.Type == ImportStages.ImportJob && job.FolderId is not null)
            .Select(job => job.FolderId!.Value)
            .ToHashSet();
        var counts = (await library.Items.CountByFolderAsync(cancellationToken).ConfigureAwait(false)).ToLookup(count => count.FolderId);
        return
        [
            .. folders.Select(folder => new IndexedFolderSummary(
                folder,
                folder.Status == FolderStatus.Offline ? IndexedFolderState.Offline
                    : importing.Contains(folder.Id) ? IndexedFolderState.Scanning
                    : IndexedFolderState.Online,
                counts[folder.Id].Where(count => count.Status != MediaItemStatus.Excluded).Sum(count => count.Count),
                counts[folder.Id].Where(count => count.Status == MediaItemStatus.Excluded).Sum(count => count.Count))),
        ];
    }
}
