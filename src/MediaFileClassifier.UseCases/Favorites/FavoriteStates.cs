using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;

namespace MediaFileClassifier.UseCases.Favorites;

/// <summary>
/// Reads the favorite state of a set of items in batches (E11-S01).
/// </summary>
internal static class FavoriteStates
{
    private const int BatchSize = 500;

    /// <summary>
    /// Finds out whether any of the items exist, and whether all the ones that exist are favorites. It stops at the
    /// first batch with a non-favorite.
    /// </summary>
    /// <param name="items">The item store.</param>
    /// <param name="ids">The item IDs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether any item exists, and whether every existing one is a favorite.</returns>
    public static async Task<(bool Any, bool AllFavorite)> ReadAsync(IMediaItemRepository items, IReadOnlyCollection<Id<MediaItem>> ids, CancellationToken cancellationToken)
    {
        var any = false;
        foreach (var batch in ids.Distinct().Chunk(BatchSize))
        {
            var summaries = await items.GetSummariesByIdsAsync(batch, cancellationToken).ConfigureAwait(false);
            any |= summaries.Count > 0;
            if (summaries.Any(summary => !summary.IsFavorite))
            {
                return (true, false);
            }
        }

        return (any, any);
    }
}
