using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Favorites.GetFavoriteState;

/// <summary>
/// Tells whether every one of the items is a favorite (E11-S01-T02), which decides whether the favorite command
/// marks or unmarks them and whether menus show it checked.
/// </summary>
/// <param name="ItemIds">The items.</param>
/// <returns><see langword="true"/> when there are items and all of them are favorites.</returns>
public sealed record GetFavoriteStateQuery(IReadOnlyCollection<Id<MediaItem>> ItemIds) : IRequest<bool>;

/// <summary>
/// Handles <see cref="GetFavoriteStateQuery"/> for the open library, in batches, and stops at the first non-favorite.
/// </summary>
/// <param name="session">Holds the open library.</param>
public sealed class GetFavoriteStateQueryHandler(ILibrarySession session) : IRequestHandler<GetFavoriteStateQuery, bool>
{
    private const int BatchSize = 500;

    /// <inheritdoc />
    public async Task<bool> Handle(GetFavoriteStateQuery request, CancellationToken cancellationToken)
    {
        if (session.Current is not { } library || request.ItemIds.Count == 0)
        {
            return false;
        }

        var found = 0;
        foreach (var batch in request.ItemIds.Chunk(BatchSize))
        {
            var summaries = await library.Items.GetSummariesByIdsAsync(batch, cancellationToken).ConfigureAwait(false);
            if (summaries.Any(summary => !summary.IsFavorite))
            {
                return false;
            }

            found += summaries.Count;
        }

        return found > 0;
    }
}
