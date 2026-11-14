using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.UseCases.Search.Filters;
using MediaFileClassifier.UseCases.Search.SearchMedia;
using MediatR;

namespace MediaFileClassifier.UseCases.Search.FindSimilar;

/// <summary>
/// "Find similar" (E09-S03-T01): the items that look like one item, by its stored embedding, most similar first.
/// The item itself isn't a result; the browser shows it first, marked Source (D053). A video or an animated image is
/// compared by its item-level vector. There's no relevance cut: the nearest candidates are all shown.
/// </summary>
/// <param name="ItemId">The item to find similar items to.</param>
public sealed record FindSimilarQuery(Id<MediaItem> ItemId) : IRequest<MediaSearchResults>
{
    /// <summary>
    /// Gets the structured filters, such as the browse scope.
    /// </summary>
    public SearchFilter Filter { get; init; } = SearchFilter.None;

    /// <summary>
    /// Gets the active filters (E10-S01). They apply on top of <see cref="Filter"/>.
    /// </summary>
    public FilterState Filters { get; init; } = FilterState.Empty;

    /// <summary>
    /// Gets the order; <see cref="SortMode.Relevance"/> is most similar first.
    /// </summary>
    public SortMode Sort { get; init; } = SortMode.Relevance;

    /// <summary>
    /// Gets whether to reverse the sort's natural direction (see <see cref="SearchQuery.ReverseSort"/>).
    /// </summary>
    public bool ReverseSort { get; init; }

    /// <summary>
    /// Gets the number of results to skip.
    /// </summary>
    public int Offset { get; init; }

    /// <summary>
    /// Gets the page size, from 1 to <see cref="SearchQuery.MaxLimit"/>.
    /// </summary>
    public int Limit { get; init; } = 100;
}
