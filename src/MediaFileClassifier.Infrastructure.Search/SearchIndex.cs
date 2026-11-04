using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.Search.Ranking;
using MediaFileClassifier.Infrastructure.Search.Vectors;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Infrastructure.Search;

/// <summary>
/// Searches the open library (E01-S04): keyword matches from the FTS5 index (<see cref="ISearchStore"/>), vector
/// matches from the in-memory index (D067), merged with Reciprocal Rank Fusion, and filtered by SQL in both paths.
/// It follows <see cref="ILibrarySession"/>: when a library opens it loads the vectors in the background, and rebuilds
/// the keyword index if the library asks for it, e.g. after the migration that added the index.
/// </summary>
public sealed class SearchIndex : ISearchIndex, IDisposable
{
    private readonly ILibrarySession session;
    private readonly ILogger<SearchIndex> logger;
    private readonly Lock sync = new();
    private LibrarySearch? current;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchIndex"/> class, and starts loading the open library's
    /// vectors if a library is open.
    /// </summary>
    /// <param name="session">The library session.</param>
    /// <param name="logger">Logger.</param>
    public SearchIndex(ILibrarySession session, ILogger<SearchIndex> logger)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(logger);

        this.session = session;
        this.logger = logger;
        session.CurrentChanged += OnCurrentChanged;
        Attach(session.Current);
    }

    /// <inheritdoc />
    public event EventHandler? VectorStateChanged;

    /// <inheritdoc />
    public VectorIndexState VectorState => current?.State ?? VectorIndexState.Unavailable;

    /// <summary>
    /// Gets a task that completes when the open library's background work at opening — the vector load and any
    /// keyword index rebuild it needed — is done. For tests and the benchmark.
    /// </summary>
    public Task Ready => current?.Started ?? Task.CompletedTask;

    /// <inheritdoc />
    public async Task<SearchResults> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Filter);
        ArgumentNullException.ThrowIfNull(query.Ranking);
        ArgumentOutOfRangeException.ThrowIfNegative(query.Offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.Limit, SearchQuery.MaxLimit);
        query.Ranking.Validate();

        var library = current;
        if (library is null)
        {
            return SearchResults.Empty;
        }

        var store = library.Library.Search;
        var terms = QueryTerms.Parse(query.Text);
        var index = query.Vector is null ? null : library.ReadyIndex;
        var skipped = query.Vector is not null && index is null;

        List<Entry> page;
        int total;
        if (query.Vector is null || index is null)
        {
            if (terms.Count == 0)
            {
                if (query.Vector is not null)
                {
                    return SearchResults.Empty with { VectorSearchSkipped = true };
                }

                var listed = await store.ListAsync(query.Filter, query.Sort, query.ReverseSort, query.Offset, query.Limit, null, cancellationToken).ConfigureAwait(false);
                return new SearchResults(
                    [.. listed.Documents.Select(document => new SearchHit(document.ItemId, 0, null, null, SearchFields.None))],
                    listed.TotalCount,
                    [],
                    false);
            }

            (page, total) = await KeywordPageAsync(store, query, terms, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            (page, total) = await RankedPageAsync(store, index, query, query.Vector, terms, cancellationToken).ConfigureAwait(false);
        }

        var fields = terms.Count > 0 && query.IncludeMatchedFields && page.Count > 0
            ? await store.GetMatchedFieldsAsync([.. page.Select(entry => entry.DocumentId)], terms, cancellationToken).ConfigureAwait(false)
            : null;
        var hits = page
            .Select(entry => new SearchHit(entry.ItemId, entry.Score, entry.KeywordRank, entry.Similarity, fields?.GetValueOrDefault(entry.DocumentId) ?? SearchFields.None))
            .ToList();
        return new SearchResults(hits, total, terms, skipped);
    }

    /// <inheritdoc />
    public async Task RebuildAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var library = current;
        if (library is null)
        {
            return;
        }

        await library.Library.Search.RebuildAsync(progress is null ? null : new ScaledProgress(progress, 0, 0.9), cancellationToken).ConfigureAwait(false);
        await library.ReloadVectorsAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(1);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        LibrarySearch? old;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            old = current;
            current = null;
        }

        session.CurrentChanged -= OnCurrentChanged;
        old?.Dispose();
    }

    private static bool[] CreateMask(int[] documentIds)
    {
        var mask = new bool[documentIds.Length == 0 ? 0 : documentIds[^1] + 1];
        foreach (var document in documentIds)
        {
            mask[document] = true;
        }

        return mask;
    }

    private static async Task<(List<Entry> Page, int Total)> KeywordPageAsync(
        ISearchStore store,
        SearchQuery query,
        IReadOnlyList<string> terms,
        CancellationToken cancellationToken)
    {
        var matches = await store.FindKeywordMatchesAsync(
            new KeywordSearch(terms, query.Filter, query.Ranking.FieldWeights, query.Sort, query.ReverseSort, query.Offset, query.Limit),
            cancellationToken).ConfigureAwait(false);
        var page = matches.Matches
            .Select((match, i) => new Entry(
                match.DocumentId,
                match.ItemId,
                double.IsNegativeInfinity(match.Score) ? 0 : match.Score,
                query.Sort != SortMode.Relevance ? null : query.ReverseSort ? matches.TotalCount - query.Offset - i : query.Offset + i + 1,
                null))
            .ToList();
        return (page, matches.TotalCount);
    }

    private static async Task<(List<Entry> Page, int Total)> RankedPageAsync(
        ISearchStore store,
        VectorIndex index,
        SearchQuery query,
        SearchVector vector,
        IReadOnlyList<string> terms,
        CancellationToken cancellationToken)
    {
        var limit = query.Ranking.CandidateLimit;
        var keywordTask = terms.Count == 0
            ? Task.FromResult(new KeywordMatches([], 0))
            : store.FindKeywordMatchesAsync(
                new KeywordSearch(terms, query.Filter, query.Ranking.FieldWeights, SortMode.Relevance, false, 0, limit),
                cancellationToken);

        // The filter runs as SQL; its documents become the mask the scorer checks (D067). Excluded items are never in it.
        var documentIds = await store.GetDocumentIdsAsync(query.Filter, cancellationToken).ConfigureAwait(false);
        var vectorHits = await Task.Run(() => index.Search(vector, limit, CreateMask(documentIds)), cancellationToken).ConfigureAwait(false);
        var keyword = await keywordTask.ConfigureAwait(false);

        var items = new Dictionary<int, Id<MediaItem>>();
        foreach (var match in keyword.Matches)
        {
            items[match.DocumentId] = match.ItemId;
        }

        var similarities = new Dictionary<int, float>();
        foreach (var hit in vectorHits)
        {
            items[hit.DocumentId] = hit.ItemId;
            similarities[hit.DocumentId] = hit.Similarity;
        }

        List<Entry> ranked;
        if (terms.Count == 0)
        {
            ranked = [.. vectorHits.Select(hit => new Entry(hit.DocumentId, hit.ItemId, hit.Similarity, null, hit.Similarity))];
        }
        else
        {
            var fused = ReciprocalRankFusion.Fuse(
                [.. keyword.Matches.Select(match => match.DocumentId)],
                [.. vectorHits.Select(hit => hit.DocumentId)],
                query.Ranking);
            ranked = [.. fused.Select(hit => new Entry(
                hit.DocumentId,
                items[hit.DocumentId],
                hit.Score,
                hit.KeywordRank,
                similarities.TryGetValue(hit.DocumentId, out var similarity) ? similarity : null))];
        }

        if (query.Sort == SortMode.Relevance)
        {
            if (query.ReverseSort)
            {
                ranked.Reverse();
            }

            return ([.. ranked.Skip(query.Offset).Take(query.Limit)], ranked.Count);
        }

        // Another order: the ranked results are the set, and SQL sorts them.
        var byDocument = ranked.ToDictionary(entry => entry.DocumentId);
        var sorted = await store.ListAsync(
            query.Filter,
            query.Sort,
            query.ReverseSort,
            query.Offset,
            query.Limit,
            byDocument.Keys,
            cancellationToken).ConfigureAwait(false);
        return ([.. sorted.Documents.Select(document => byDocument[document.DocumentId])], ranked.Count);
    }

    private void OnCurrentChanged(object? sender, EventArgs e) => Attach(session.Current);

    private void Attach(IOpenLibrary? library)
    {
        LibrarySearch? old;
        LibrarySearch? created = null;
        lock (sync)
        {
            if (disposed || ReferenceEquals(current?.Library, library))
            {
                return;
            }

            old = current;
            current = library is null ? null : created = new LibrarySearch(library, logger, RaiseVectorStateChanged);
        }

        old?.Dispose();
        created?.Start();
        RaiseVectorStateChanged();
    }

    private void RaiseVectorStateChanged() => VectorStateChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// A result before it's paged and given its matched fields.
    /// </summary>
    private sealed record Entry(int DocumentId, Id<MediaItem> ItemId, double Score, int? KeywordRank, float? Similarity);

    /// <summary>
    /// Maps progress into a sub-range, synchronously.
    /// </summary>
    private sealed class ScaledProgress(IProgress<double> inner, double from, double to) : IProgress<double>
    {
        public void Report(double value) => inner.Report(from + ((to - from) * value));
    }

    /// <summary>
    /// The search state of one open library: its vector index, kept current from the embedding repository's commits.
    /// </summary>
    private sealed class LibrarySearch : IDisposable
    {
        private readonly ILogger logger;
        private readonly Action stateChanged;
        private readonly Lock gate = new();
        private readonly CancellationTokenSource stopping = new();
        private List<IReadOnlyList<CommittedEmbeddings>>? pending;
        private volatile VectorIndex? index;
        private volatile int state = (int)VectorIndexState.Loading;

        public LibrarySearch(IOpenLibrary library, ILogger logger, Action stateChanged)
        {
            Library = library;
            this.logger = logger;
            this.stateChanged = stateChanged;
        }

        public IOpenLibrary Library { get; }

        public VectorIndexState State => (VectorIndexState)state;

        public VectorIndex? ReadyIndex => State == VectorIndexState.Ready ? index : null;

        public Task Started { get; private set; } = Task.CompletedTask;

        public void Start()
        {
            Library.Embeddings.Changed += OnEmbeddingsChanged;
            Started = Task.Run(StartAsync);
        }

        /// <summary>
        /// Loads every vector into a new index while the current one keeps answering, then swaps them. Commits that
        /// arrive during the load are applied to both: replaying a commit the load already read is harmless, because
        /// every change replaces all of an item's vectors.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when the new index is in use.</returns>
        public async Task ReloadVectorsAsync(CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping.Token);
            lock (gate)
            {
                pending = [];
            }

            VectorIndex loaded;
            try
            {
                loaded = await VectorIndex.LoadAsync(Library.Embeddings, linked.Token).ConfigureAwait(false);
            }
            catch
            {
                lock (gate)
                {
                    pending = null;
                }

                throw;
            }

            lock (gate)
            {
                foreach (var changes in pending ?? [])
                {
                    loaded.Apply(changes);
                }

                pending = null;

                // The old index isn't disposed: a search may still be reading it. It's garbage once that's done.
                index = loaded;
            }

            SetState(VectorIndexState.Ready);
        }

        public void Dispose()
        {
            Library.Embeddings.Changed -= OnEmbeddingsChanged;
            stopping.Cancel();
            stopping.Dispose();
            SetState(VectorIndexState.Unavailable, raise: false);
        }

        private async Task StartAsync()
        {
            try
            {
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                await ReloadVectorsAsync(CancellationToken.None).ConfigureAwait(false);
                logger.LogInformation(
                    "Loaded {VectorCount} vectors for search in {ElapsedMs} ms",
                    index?.VectorCount ?? 0,
                    (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            catch (Exception exception) when (stopping.IsCancellationRequested || exception is ObjectDisposedException)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Loading the vectors for search failed; keyword search still works");
                SetState(VectorIndexState.Failed);
            }

            try
            {
                if (await Library.Search.IsRebuildNeededAsync(stopping.Token).ConfigureAwait(false))
                {
                    logger.LogInformation("Rebuilding the keyword search index");
                    await Library.Search.RebuildAsync(null, stopping.Token).ConfigureAwait(false);
                    logger.LogInformation("Rebuilt the keyword search index");
                }
            }
            catch (Exception exception) when (stopping.IsCancellationRequested || exception is ObjectDisposedException)
            {
                // The library closed; the index stays marked and is rebuilt next time.
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Rebuilding the keyword search index failed");
            }
        }

        private void OnEmbeddingsChanged(object? sender, EmbeddingsChangedEventArgs e)
        {
            try
            {
                lock (gate)
                {
                    pending?.Add(e.Changes);
                    index?.Apply(e.Changes);
                }
            }
            catch (Exception exception)
            {
                // Raised on the writer's side; never let the index fail the write that already committed.
                logger.LogError(exception, "Updating the vector index failed; a rebuild repairs it");
            }
        }

        private void SetState(VectorIndexState value, bool raise = true)
        {
            if (Interlocked.Exchange(ref state, (int)value) != (int)value && raise)
            {
                stateChanged();
            }
        }
    }
}
