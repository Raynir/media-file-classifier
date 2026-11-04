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
    /// <param name="offset">Number of results to skip, counted from <paramref name="newest"/> when it's given.</param>
    /// <param name="limit">Maximum number of IDs.</param>
    /// <param name="newest">If given, only this result and the ones added before it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The IDs.</returns>
    Task<IReadOnlyList<Id<MediaItem>>> GetIdsAsync(int offset, int limit, Id<MediaItem>? newest, CancellationToken cancellationToken);

    /// <summary>
    /// Finds which of the given items are still in the results (E14-S03), so a selection survives a refresh.
    /// </summary>
    /// <param name="ids">The item IDs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The IDs still in the results.</returns>
    Task<IReadOnlySet<Id<MediaItem>>> GetExistingIdsAsync(IReadOnlyCollection<Id<MediaItem>> ids, CancellationToken cancellationToken);
}
