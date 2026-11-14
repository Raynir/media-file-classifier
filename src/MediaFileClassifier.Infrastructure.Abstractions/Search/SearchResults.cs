using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Abstractions.Search;

/// <summary>
/// One page of search results.
/// </summary>
/// <param name="Hits">The results of the page, in order.</param>
/// <param name="TotalCount">How many results the query has over all pages. Vector and hybrid results stop at
/// <see cref="SearchRankingOptions.CandidateLimit"/> per list.</param>
/// <param name="Terms">The words the text was split into, for highlighting; empty without text.</param>
/// <param name="VectorSearchSkipped">Whether the query had a vector that couldn't be used yet, because the vector
/// index is still loading or failed (<see cref="ISearchIndex.VectorState"/>). The results then come from the text
/// alone.</param>
public sealed record SearchResults(IReadOnlyList<SearchHit> Hits, int TotalCount, IReadOnlyList<string> Terms, bool VectorSearchSkipped)
{
    /// <summary>
    /// Gets the empty result.
    /// </summary>
    public static SearchResults Empty { get; } = new([], 0, [], false);

    /// <summary>
    /// Gets how many results fall below <see cref="SearchQuery.RelevanceCut"/>. They're left out of
    /// <see cref="Hits"/> and <see cref="TotalCount"/> unless <see cref="SearchQuery.IncludeLessRelevant"/> is set.
    /// </summary>
    public int LessRelevantCount { get; init; }
}

/// <summary>
/// One search result.
/// </summary>
/// <param name="ItemId">The item.</param>
/// <param name="Score">Relevance, higher is better: the RRF score in hybrid queries, the cosine similarity in vector
/// queries, the negated BM25 in keyword queries, and 0 when nothing ranks the items.</param>
/// <param name="KeywordRank">Rank in the keyword list, from 1, or <c>null</c> if the item isn't in it.</param>
/// <param name="Similarity">Cosine similarity of the item's best vector to the query vector, or <c>null</c>.</param>
/// <param name="MatchedFields">Which fields the text matched, for highlighting.</param>
public sealed record SearchHit(
    Id<MediaItem> ItemId,
    double Score,
    int? KeywordRank,
    float? Similarity,
    SearchFields MatchedFields)
{
    /// <summary>
    /// Gets whether only the vector search found the item, with a similarity below the query's
    /// <see cref="SearchQuery.RelevanceCut"/>.
    /// </summary>
    public bool IsLessRelevant { get; init; }
}
