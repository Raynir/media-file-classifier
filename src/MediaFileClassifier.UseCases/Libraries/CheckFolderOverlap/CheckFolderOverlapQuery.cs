using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Libraries.CheckFolderOverlap;

/// <summary>
/// Checks how a folder the user picked overlaps the open library's folders, so the UI can refuse it or ask before
/// merging (E02-S03-T02).
/// </summary>
/// <param name="Folder">The picked folder.</param>
public sealed record CheckFolderOverlapQuery(FolderHandle Folder) : IRequest<FolderOverlap>;

/// <summary>
/// Handles <see cref="CheckFolderOverlapQuery"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="files">Compares folder handles.</param>
public sealed class CheckFolderOverlapQueryHandler(ILibrarySession session, IMediaFileSource files) : IRequestHandler<CheckFolderOverlapQuery, FolderOverlap>
{
    /// <inheritdoc />
    public async Task<FolderOverlap> Handle(CheckFolderOverlapQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var library = session.Current ?? throw new InvalidOperationException("No library is open.");
        return IndexedFolders.FindOverlap(files, request.Folder, await IndexedFolders.GetActiveAsync(library, cancellationToken).ConfigureAwait(false));
    }
}
