using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Searches;

namespace MediaFileClassifier.Infrastructure.Abstractions.Search;

/// <summary>
/// The SQL side of search in an open library: the FTS5 keyword index, filters and ordering (E01-S04-T02, T05).
/// <see cref="ISearchIndex"/> builds on it. Items are identified by their document ID, a stable integer that the
/// keyword and vector indexes share; a deleted item's ID is never reused.
/// </summary>
public interface ISearchStore
{
    /// <summary>
    /// Finds the items whose keyword fields match every term, as a word prefix in any field or as a substring of the
    /// path for terms of three or more characters, and that pass the filter.
    /// </summary>
    /// <param name="search">The terms, filter, order and page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page of matches and the number of all matches.</returns>
    Task<KeywordMatches> FindKeywordMatchesAsync(KeywordSearch search, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the documents that pass a filter, for the vector path to score (D067).
    /// </summary>
    /// <param name="filter">The filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document IDs, ascending.</returns>
    Task<int[]> GetDocumentIdsAsync(SearchFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the documents that pass a filter, in a given order.
    /// </summary>
    /// <param name="filter">The filter.</param>
    /// <param name="sort">The order. <see cref="SortMode.Relevance"/> lists the newest imports first.</param>
    /// <param name="reverse">Whether to reverse the order.</param>
    /// <param name="offset">Number of documents to skip.</param>
    /// <param name="limit">Maximum number of documents to return.</param>
    /// <param name="onlyDocumentIds">If given, only these documents, e.g. vector results to sort by date.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page and the number of documents over all pages.</returns>
    Task<DocumentPage> ListAsync(
        SearchFilter filter,
        SortMode sort,
        bool reverse,
        int offset,
        int limit,
        IReadOnlyCollection<int>? onlyDocumentIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds which fields of some documents match any of the terms, for highlighting.
    /// </summary>
    /// <param name="documentIds">The documents.</param>
    /// <param name="terms">The terms, as given to <see cref="FindKeywordMatchesAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matched fields of each document that matched any.</returns>
    Task<IReadOnlyDictionary<int, SearchFields>> GetMatchedFieldsAsync(
        IReadOnlyCollection<int> documentIds,
        IReadOnlyList<string> terms,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the keyword index must be rebuilt, e.g. after the schema migration that added it, whose
    /// metadata columns start empty.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if a rebuild is needed.</returns>
    Task<bool> IsRebuildNeededAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds the keyword index from the source tables, recreating its tables and triggers if they're missing. It
    /// writes in batches, each its own short write (D072).
    /// </summary>
    /// <param name="progress">Receives the share of items done, from 0 to 1.</param>
    /// <param name="cancellationToken">Cancellation token. Batches already written stay written, and the index stays
    /// marked for a rebuild.</param>
    /// <returns>A task that completes when the index is rebuilt.</returns>
    Task RebuildAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// A keyword query for <see cref="ISearchStore.FindKeywordMatchesAsync"/>.
/// </summary>
/// <param name="Terms">The words; at least one. Each is matched as a whole, punctuation included.</param>
/// <param name="Filter">The filter.</param>
/// <param name="Weights">BM25 weights of the fields.</param>
/// <param name="Sort">The order; <see cref="SortMode.Relevance"/> is BM25.</param>
/// <param name="Reverse">Whether to reverse the order.</param>
/// <param name="Offset">Number of matches to skip.</param>
/// <param name="Limit">Maximum number of matches to return.</param>
public sealed record KeywordSearch(
    IReadOnlyList<string> Terms,
    SearchFilter Filter,
    KeywordFieldWeights Weights,
    SortMode Sort,
    bool Reverse,
    int Offset,
    int Limit);

/// <summary>
/// A page of keyword matches.
/// </summary>
/// <param name="Matches">The matches of the page, in order.</param>
/// <param name="TotalCount">The number of matches over all pages.</param>
public sealed record KeywordMatches(IReadOnlyList<KeywordMatch> Matches, int TotalCount);

/// <summary>
/// One keyword match.
/// </summary>
/// <param name="DocumentId">Document ID.</param>
/// <param name="ItemId">Item ID.</param>
/// <param name="Score">Negated BM25, higher is better; <see cref="double.NegativeInfinity"/> when only path substrings
/// matched, which ranks below every word match.</param>
public sealed record KeywordMatch(int DocumentId, Id<MediaItem> ItemId, double Score);

/// <summary>
/// A page of documents.
/// </summary>
/// <param name="Documents">The documents of the page, in order.</param>
/// <param name="TotalCount">The number of documents over all pages.</param>
public sealed record DocumentPage(IReadOnlyList<SearchDocument> Documents, int TotalCount);

/// <summary>
/// An item with its document ID.
/// </summary>
/// <param name="DocumentId">Document ID.</param>
/// <param name="ItemId">Item ID.</param>
public sealed record SearchDocument(int DocumentId, Id<MediaItem> ItemId);
