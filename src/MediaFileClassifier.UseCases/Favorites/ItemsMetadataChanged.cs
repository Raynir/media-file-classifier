using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediatR;

namespace MediaFileClassifier.UseCases.Favorites;

/// <summary>
/// What kind of user value changed on items.
/// </summary>
public enum ItemMetadataChange
{
    /// <summary>The favorite mark changed.</summary>
    Favorite,
}

/// <summary>
/// Published after user values of items changed (E11-S01-T01), so the grid, the details panel and the filters can
/// update in place. The metadata service (E07-S01) is expected to publish it for tags, title and description too.
/// </summary>
/// <remarks>Handlers run on the caller's thread, so UI handlers dispatch to the UI thread themselves.</remarks>
/// <param name="ItemIds">The items whose value changed.</param>
/// <param name="Change">What changed.</param>
/// <param name="IsFavorite">For <see cref="ItemMetadataChange.Favorite"/>: the new value, the same for all items.</param>
public sealed record ItemsMetadataChanged(IReadOnlyList<Id<MediaItem>> ItemIds, ItemMetadataChange Change, bool IsFavorite = false) : INotification;
