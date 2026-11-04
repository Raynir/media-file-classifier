using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Browsing.Subfolders;

/// <summary>
/// Lists the subfolders of a folder's subfolder for the folders tree (E13-S02-T03), from the paths of its items, so it
/// works for Offline folders too. Empty when no library is open.
/// </summary>
/// <param name="FolderId">The indexed folder.</param>
/// <param name="Subfolder">The subfolder whose subfolders to list; empty for the folder itself.</param>
public sealed record GetSubfoldersQuery(Id<IndexedFolder> FolderId, string Subfolder) : IRequest<IReadOnlyList<SubfolderSummary>>;

/// <summary>
/// A subfolder in the folders tree.
/// </summary>
/// <param name="Name">Its name.</param>
/// <param name="Path">Its path in the indexed folder, with <c>/</c> separators.</param>
/// <param name="ItemCount">Its items, the ones in its subfolders included.</param>
public sealed record SubfolderSummary(string Name, string Path, int ItemCount);

/// <summary>
/// Handles <see cref="GetSubfoldersQuery"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
public sealed class GetSubfoldersQueryHandler(ILibrarySession session) : IRequestHandler<GetSubfoldersQuery, IReadOnlyList<SubfolderSummary>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SubfolderSummary>> Handle(GetSubfoldersQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (session.Current is not { } library)
        {
            return [];
        }

        var parent = request.Subfolder.Trim('/');
        var subfolders = await library.Items.GetSubfoldersAsync(request.FolderId, parent, cancellationToken).ConfigureAwait(false);
        return [.. subfolders.Select(subfolder => new SubfolderSummary(subfolder.Name, parent.Length == 0 ? subfolder.Name : $"{parent}/{subfolder.Name}", subfolder.Count))];
    }
}
