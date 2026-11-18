using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Favorites.ToggleFavorite;

/// <summary>
/// Toggles the favorite mark of one or more items (E11-S01-T01). When every item is a favorite already, they are all
/// unmarked; otherwise (none, or a mix) they are all marked.
/// </summary>
/// <param name="ItemIds">The items.</param>
/// <returns>The new value of the items, or <see langword="null"/> if no library is open or none of the items exist.</returns>
public sealed record ToggleFavoriteCommand(IReadOnlyCollection<Id<MediaItem>> ItemIds) : IRequest<bool?>;

/// <summary>
/// Handles <see cref="ToggleFavoriteCommand"/> and publishes <see cref="ItemsMetadataChanged"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="publisher">Publishes the change.</param>
/// <param name="clock">The clock, for the change time.</param>
public sealed class ToggleFavoriteCommandHandler(ILibrarySession session, IPublisher publisher, TimeProvider clock)
    : IRequestHandler<ToggleFavoriteCommand, bool?>
{
    /// <inheritdoc />
    public async Task<bool?> Handle(ToggleFavoriteCommand request, CancellationToken cancellationToken)
    {
        if (session.Current is not { } library || request.ItemIds.Count == 0)
        {
            return null;
        }

        var items = new List<MediaItem>();
        foreach (var id in request.ItemIds.Distinct())
        {
            if (await library.Items.GetAsync(id, cancellationToken).ConfigureAwait(false) is { } item)
            {
                items.Add(item);
            }
        }

        if (items.Count == 0)
        {
            return null;
        }

        var value = !items.All(item => item.IsFavorite);
        var now = clock.GetUtcNow();
        var changed = items.Where(item => item.IsFavorite != value).ToList();
        foreach (var item in changed)
        {
            item.SetFavorite(value, now);
        }

        if (changed.Count > 0)
        {
            await library.Items.UpdateRangeAsync(changed, cancellationToken).ConfigureAwait(false);
            await publisher.Publish(
                new ItemsMetadataChanged([.. changed.Select(item => item.Id)], ItemMetadataChange.Favorite, value),
                cancellationToken).ConfigureAwait(false);
        }

        return value;
    }
}
