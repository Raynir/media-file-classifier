using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;

namespace MediaFileClassifier.UseCases.Browsing;

/// <summary>
/// The paged results the media browser shows (E14-S01-T02). In M1 it's every item of the library, newest first
/// (<see cref="LibraryItems.LibraryItemsSource"/>); from M2 search and filters provide results through the same
/// interface, so the grid doesn't change.
/// </summary>
public interface IMediaResultSource
{
    /// <summary>
    /// Raised when the results may have changed, e.g. items were added or a library was opened. It can be raised on
    /// any thread and often; the browser coalesces it.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Counts the results.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of results, or 0 when there's nothing to show.</returns>
    Task<int> CountAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets a page of results.
    /// </summary>
    /// <param name="offset">Position of the first result.</param>
    /// <param name="limit">Maximum number of results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The results; fewer than asked at the end.</returns>
    Task<IReadOnlyList<MediaItemSummary>> GetPageAsync(int offset, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Finds where an item is now, so the browser can keep it in place when results change.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Its position, or where it would be if it's gone.</returns>
    Task<int> PositionOfAsync(Id<MediaItem> itemId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets result IDs in result order without loading the items (E14-S03), for range selection and Select all.
    /// </summary>
    /// <param name="offset">Number of results to skip, counted among the ones <paramref name="newest"/> lets through.</param>
    /// <param name="limit">Maximum number of IDs.</param>
    /// <param name="newest">If given, only this result and the ones added before it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The IDs.</returns>
    Task<IReadOnlyList<Id<MediaItem>>> GetIdsAsync(int offset, int limit, Id<MediaItem>? newest, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the newest item that is or could be in the results now (E10-S04-T02): the boundary of Select all, which
    /// covers every result added up to it, in any order. The default suits results listed newest first.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item, or <see langword="null"/> when there are no results.</returns>
    async Task<Id<MediaItem>?> GetNewestIdAsync(CancellationToken cancellationToken)
    {
        var first = await GetIdsAsync(0, 1, null, cancellationToken).ConfigureAwait(false);
        return first.Count == 0 ? null : first[0];
    }

    /// <summary>
    /// Counts the results added after an item (E10-S04-T02), e.g. since Select all. The default suits results listed
    /// newest first, where that is the item's position.
    /// </summary>
    /// <param name="newest">The item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The count.</returns>
    Task<int> CountNewerAsync(Id<MediaItem> newest, CancellationToken cancellationToken) => PositionOfAsync(newest, cancellationToken);

    /// <summary>
    /// Finds which of the given items are still in the results (E14-S03), so a selection survives a refresh.
    /// </summary>
    /// <param name="ids">The item IDs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The IDs still in the results.</returns>
    Task<IReadOnlySet<Id<MediaItem>>> GetExistingIdsAsync(IReadOnlyCollection<Id<MediaItem>> ids, CancellationToken cancellationToken);
}
