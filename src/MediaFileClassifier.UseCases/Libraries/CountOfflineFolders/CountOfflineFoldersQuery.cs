using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Libraries.CountOfflineFolders;

/// <summary>
/// Counts the indexed folders of the open library that can't be reached (E13-S04-T02). 0 when no library is open.
/// </summary>
public sealed record CountOfflineFoldersQuery : IRequest<int>;

/// <summary>
/// Handles <see cref="CountOfflineFoldersQuery"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
public sealed class CountOfflineFoldersQueryHandler(ILibrarySession session) : IRequestHandler<CountOfflineFoldersQuery, int>
{
    /// <inheritdoc />
    public async Task<int> Handle(CountOfflineFoldersQuery request, CancellationToken cancellationToken)
    {
        if (session.Current is not { } library)
        {
            return 0;
        }

        var folders = await library.Folders.GetAllAsync(cancellationToken);
        return folders.Count(folder => folder.Status == FolderStatus.Offline);
    }
}
