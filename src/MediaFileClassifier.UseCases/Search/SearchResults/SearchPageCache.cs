using MediaFileClassifier.UseCases.Search.SearchMedia;

namespace MediaFileClassifier.UseCases.Search.SearchResults;

/// <summary>
/// Keeps the first page of the results of recent text searches (E09-S02-T02), so going back to a text, for example by
/// deleting a character, shows its results at once. The oldest used entry goes first when the cache is full. Safe to
/// use from any thread.
/// </summary>
internal sealed class SearchPageCache
{
    /// <summary>
    /// The number of searches kept. A page is at most <see cref="MaxCachedLimit"/> hits, so this stays small.
    /// </summary>
    public const int Capacity = 32;

    /// <summary>
    /// The largest page that is kept: the pages the grid asks for, not the reading of every ID of a search.
    /// </summary>
    public const int MaxCachedLimit = 200;

    private readonly Lock gate = new();
    private readonly Dictionary<SearchMediaQuery, LinkedListNode<Entry>> entries = [];
    private readonly LinkedList<Entry> recent = new();
    private int version;

    /// <summary>
    /// Gets a number that changes when the cache is cleared; an answer read before it changed is stale.
    /// </summary>
    public int Version
    {
        get
        {
            lock (gate)
            {
                return version;
            }
        }
    }

    /// <summary>
    /// Gets the number of cached searches.
    /// </summary>
    public int Count
    {
        get
        {
            lock (gate)
            {
                return entries.Count;
            }
        }
    }

    /// <summary>
    /// Tells whether the answer to a query may be kept: the first page of a text search, and not one of the answers that
    /// say the search could do better soon.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <returns><see langword="true"/> when its answer may be cached.</returns>
    public static bool CanCache(SearchMediaQuery query) =>
        query is { Offset: 0, ReferenceImage: null, Limit: <= MaxCachedLimit } && !string.IsNullOrEmpty(query.Text);

    /// <summary>
    /// Tells whether an answer may be kept. Results that left out the semantic search for a reason that can pass (the
    /// vectors are still loading, the model isn't there yet, an error) are run again the next time.
    /// </summary>
    /// <param name="results">The answer.</param>
    /// <returns><see langword="true"/> when it may be cached.</returns>
    public static bool CanCache(MediaSearchResults results) =>
        results.Semantic is not (SemanticSearchStatus.IndexNotReady or SemanticSearchStatus.ModelMissing or SemanticSearchStatus.Failed);

    /// <summary>
    /// Gets the answer to a query.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <param name="results">The answer, when it's cached.</param>
    /// <returns><see langword="true"/> when it's cached.</returns>
    public bool TryGet(SearchMediaQuery query, out MediaSearchResults results)
    {
        lock (gate)
        {
            if (entries.TryGetValue(query, out var node))
            {
                recent.Remove(node);
                recent.AddFirst(node);
                results = node.Value.Results;
                return true;
            }
        }

        results = null!;
        return false;
    }

    /// <summary>
    /// Keeps the answer to a query.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <param name="results">The answer.</param>
    /// <param name="readAt">The <see cref="Version"/> from before the search started: an answer that began before a
    /// clear is not kept.</param>
    public void Set(SearchMediaQuery query, MediaSearchResults results, int readAt)
    {
        lock (gate)
        {
            if (readAt != version)
            {
                return;
            }

            if (entries.Remove(query, out var old))
            {
                recent.Remove(old);
            }

            entries[query] = recent.AddFirst(new Entry(query, results));
            while (entries.Count > Capacity)
            {
                var oldest = recent.Last!;
                recent.RemoveLast();
                entries.Remove(oldest.Value.Query);
            }
        }
    }

    /// <summary>
    /// Forgets everything, because the items or the scope changed.
    /// </summary>
    public void Clear()
    {
        lock (gate)
        {
            version++;
            entries.Clear();
            recent.Clear();
        }
    }

    private sealed record Entry(SearchMediaQuery Query, MediaSearchResults Results);
}
