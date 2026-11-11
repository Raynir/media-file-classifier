using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediatR;

namespace MediaFileClassifier.UseCases.Search.SearchMedia;

/// <summary>
/// The unified text search (E09-S01): one query that runs the keyword and the semantic search together and merges
/// them, or lists the items when the text is empty (browse mode).
/// </summary>
public sealed record SearchMediaQuery : IRequest<MediaSearchResults>
{
    /// <summary>
    /// Gets what the user typed, as plain text (D022). <c>null</c>, empty or white space browses every item.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Gets the structured filters. They apply to every mode.
    /// </summary>
    public SearchFilter Filter { get; init; } = SearchFilter.None;

    /// <summary>
    /// Gets the order. <see cref="SortMode.Relevance"/> in browse mode means date taken, newest first (E10-S04).
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

    /// <summary>
    /// Gets whether to report which fields matched each hit, for highlighting and match reasons.
    /// </summary>
    public bool IncludeMatchedFields { get; init; } = true;

    /// <summary>
    /// Gets whether to include the semantic matches below the model's relevance threshold ("Show less relevant
    /// results", E09-S01-T03). They come back marked <see cref="SearchHit.IsLessRelevant"/>, in rank order.
    /// </summary>
    public bool ShowLessRelevant { get; init; }
}

/// <summary>
/// One page of unified search results.
/// </summary>
/// <param name="Hits">The results of the page, in order.</param>
/// <param name="TotalCount">How many results the search has over all pages; without the less relevant ones unless
/// <see cref="SearchMediaQuery.ShowLessRelevant"/> is set.</param>
/// <param name="Terms">The words the keyword search used, for highlighting; empty in browse mode.</param>
/// <param name="Mode">Which searches produced the results.</param>
/// <param name="Semantic">Whether the semantic search ran, and why not if it didn't.</param>
/// <param name="LessRelevantCount">How many semantic matches fall below the relevance threshold, shown or not: the
/// N of "Show N less relevant results".</param>
public sealed record MediaSearchResults(
    IReadOnlyList<SearchHit> Hits,
    int TotalCount,
    IReadOnlyList<string> Terms,
    SearchMode Mode,
    SemanticSearchStatus Semantic,
    int LessRelevantCount = 0);

/// <summary>
/// Which searches produced a result list.
/// </summary>
public enum SearchMode
{
    /// <summary>
    /// The text was empty: every item that passes the filters.
    /// </summary>
    Browse,

    /// <summary>
    /// The keyword search alone, because the semantic search couldn't run.
    /// </summary>
    Keyword,

    /// <summary>
    /// The semantic search alone, because the text has no word the keyword search can use (only symbols, say).
    /// </summary>
    Semantic,

    /// <summary>
    /// Both searches, merged with reciprocal rank fusion (E01-S04-T04).
    /// </summary>
    Hybrid,

    /// <summary>
    /// "Find similar" (E09-S03): a vector search from an item's own embedding.
    /// </summary>
    Similar,
}

/// <summary>
/// Whether the semantic part of a search ran.
/// </summary>
public enum SemanticSearchStatus
{
    /// <summary>
    /// Browse mode has nothing to encode.
    /// </summary>
    NotNeeded,

    /// <summary>
    /// The query was encoded and compared with the image embeddings.
    /// </summary>
    Used,

    /// <summary>
    /// No library is open, or no text embedding provider is registered.
    /// </summary>
    Unavailable,

    /// <summary>
    /// The embedding model isn't installed yet (D037): keyword results only.
    /// </summary>
    ModelMissing,

    /// <summary>
    /// The vector index is still loading or failed to load: keyword results only, show a hint.
    /// </summary>
    IndexNotReady,

    /// <summary>
    /// Encoding the query failed; the error is logged. Keyword results only.
    /// </summary>
    Failed,

    /// <summary>
    /// "Find similar" on an item without an embedding yet: nothing to compare until it's analyzed (D036).
    /// </summary>
    NotAnalyzed,
}
