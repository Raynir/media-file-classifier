using System.Collections.Concurrent;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.UseCases.Browsing;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
using MediaFileClassifier.UseCases.Browsing.Selection;
using MediaFileClassifier.UseCases.Search.Filters;
using MediaFileClassifier.UseCases.Search.FindSimilar;
using MediaFileClassifier.UseCases.Search.LoadReferenceImage;
using MediaFileClassifier.UseCases.Search.SearchMedia;
using MediatR;

namespace MediaFileClassifier.UseCases.Search.SearchResults;

/// <summary>
/// What the search showed last, for the notice above the results (E09-S01-T05).
/// </summary>
/// <param name="Text">The search text, or an empty string for an image search.</param>
/// <param name="Mode">Which searches produced the results.</param>
/// <param name="Semantic">Whether the semantic search ran, and why not if it didn't.</param>
/// <param name="TotalCount">The number of results shown.</param>
/// <param name="LessRelevantCount">The N of "Show N less relevant results".</param>
/// <param name="ShowLessRelevant">Whether the less relevant results are included.</param>
/// <param name="SimilarTo">The item a "find similar" search started from, or <see langword="null"/>.</param>
/// <param name="SkippedCount">How many Outdated items the semantic search skipped (D051, E06-S07-T05).</param>
public sealed record SearchSummary(
    string Text,
    SearchMode Mode,
    SemanticSearchStatus Semantic,
    int TotalCount,
    int LessRelevantCount,
    bool ShowLessRelevant,
    SimilarSource? SimilarTo = null,
    int SkippedCount = 0);

/// <summary>
/// The item a "find similar" search (E09-S03) started from.
/// </summary>
/// <param name="ItemId">The item.</param>
/// <param name="Name">Its file name, for the chip ("Similar to: IMG_1234").</param>
/// <param name="FileModifiedAt">Its file modification time, to load the chip's thumbnail.</param>
public sealed record SimilarSource(Id<MediaItem> ItemId, string Name, DateTimeOffset FileModifiedAt = default);

/// <summary>
/// The result source of the media browser from M2 (E09-S01-T05): the unified text search while the search text has
/// something to look for, the image-to-image search while a reference image is set (E09-S04), and the browse results
/// of <see cref="LibraryItemsSource"/> otherwise. Changing the text, the reference image, the less relevant option or
/// the browse scope starts a new search and cancels the one still running. A search covers the selected scope (folder,
/// subfolder, recently imported) and the active filters (E10-S01). With filters set and nothing to search for, or with
/// any browse order other than newest import first (D177), the browse results come from the search index too.
/// The order is remembered separately for searching and for browsing (E10-S04-T02): <see cref="SetSort"/> changes the one
/// in use, which depends on whether there's something to search for.
/// "Find similar" (E09-S03) is the third kind of search: <see cref="SetSimilarTo"/> lists the items like one item, with
/// that item first, marked Source; it doesn't count in the summary's total (D053). Text typed or a reference image
/// replaces it, and it replaces a reference image.
/// </summary>
public sealed class SearchResultSource : IMediaResultSource, IResultAnnotations
{
    private const int IdPageSize = SearchQuery.MaxLimit;

    private readonly LibraryItemsSource browse;
    private readonly ISender sender;
    private readonly FilterRegistry? filterRegistry;
    private readonly Lock syncRoot = new();
    private readonly SearchPageCache pages = new();
    private SortChoice searchSort = SortChoice.SearchDefault;
    private SortChoice browseSort = SortChoice.BrowseDefault;
    private ActiveSearch current;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchResultSource"/> class.
    /// </summary>
    /// <param name="browse">The results while there's no search text.</param>
    /// <param name="sender">Sends the queries.</param>
    /// <param name="filterRegistry">The registered filters, to tell which active filters apply; without it every
    /// active filter counts.</param>
    public SearchResultSource(LibraryItemsSource browse, ISender sender, FilterRegistry? filterRegistry = null)
    {
        this.browse = browse;
        this.sender = sender;
        this.filterRegistry = filterRegistry;

        // Each source has its own: browsing by date goes through the search, and replacing it cancels it.
        current = Create(string.Empty, showLessRelevant: false, SearchFilter.None);
        foreach (var filter in filterRegistry?.All ?? [])
        {
            // A filter that comes or goes changes what an active filter state means: search again.
            filter.AvailabilityChanged += (_, _) =>
            {
                if (current.Filters.Get(filter.Id) is not null)
                {
                    Replace(old => Create(old.Text, old.ShowLessRelevant, ScopeFilter(), old.Image, old.Similar, old.Filters));
                }
            };
        }

        browse.Changed += (_, _) =>
        {
            // The scope or the items changed: a search starts over inside the new scope, on the current items.
            pages.Clear();
            Restart();
            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>
    /// Raised, on any thread, when <see cref="Summary"/> changes.
    /// </summary>
    public event EventHandler? SummaryChanged;

    /// <summary>
    /// Gets the search text as typed, or an empty string while browsing.
    /// </summary>
    public string Text => current.Text;

    /// <summary>
    /// Gets the picture being searched with, or <see langword="null"/> for a text search or while browsing.
    /// </summary>
    public ReferenceImage? ReferenceImage => current.Image;

    /// <summary>
    /// Gets a value indicating whether the less relevant semantic matches are included.
    /// </summary>
    public bool ShowLessRelevant => current.ShowLessRelevant;

    /// <summary>
    /// Gets the item the "find similar" search started from, or <see langword="null"/> when it isn't one.
    /// </summary>
    public SimilarSource? SimilarTo => current.Similar;

    /// <summary>
    /// Gets the active filters (E10-S01). They apply to every kind of search and to browsing.
    /// </summary>
    public FilterState Filters => current.Filters;

    /// <summary>
    /// Gets a value indicating whether there's something to search for (text, a reference image or a "find similar"
    /// item), so the results use <see cref="SearchSort"/> and can be sorted by relevance; otherwise they use
    /// <see cref="BrowseSort"/>.
    /// </summary>
    public bool IsSearchContext => current.IsSearchContext;

    /// <summary>
    /// Gets the order of the results shown now: <see cref="SearchSort"/> or <see cref="BrowseSort"/>.
    /// </summary>
    public SortChoice Sort => current.Sort;

    /// <summary>
    /// Gets the order used while searching; relevance by default.
    /// </summary>
    public SortChoice SearchSort => searchSort;

    /// <summary>
    /// Gets the order used while browsing; date taken, newest first, by default.
    /// </summary>
    public SortChoice BrowseSort => browseSort;

    /// <summary>
    /// Gets what the search showed last, or <see langword="null"/> while browsing.
    /// </summary>
    public SearchSummary? Summary { get; private set; }

    /// <summary>
    /// Starts a search for the text, or goes back to browsing when it's blank. The text is searched as it is
    /// (<see cref="SearchText.Normalise"/> runs in the search); the less relevant option resets. Text replaces a
    /// reference image, but a blank text leaves the image search as it is.
    /// </summary>
    /// <param name="text">What the user typed.</param>
    public void SetText(string? text)
    {
        var value = string.IsNullOrWhiteSpace(text) ? string.Empty : text;

        // Blank text leaves an image-to-image or "find similar" search alone: only text to look for replaces them.
        Replace(old => old.Text == value || (value.Length == 0 && (old.Image is not null || old.Similar is not null))
            ? null
            : Create(value, showLessRelevant: false, ScopeFilter(), filters: old.Filters));
    }

    /// <summary>
    /// Starts an image-to-image search with the picture (E09-S04), replacing the text or a "find similar" search.
    /// <see langword="null"/> removes the picture and goes back to browsing; it does nothing to another search.
    /// </summary>
    /// <param name="image">The picture, from <see cref="LoadReferenceImageQuery"/>.</param>
    public void SetReferenceImage(ReferenceImage? image) =>
        Replace(old => ReferenceEquals(old.Image, image) || (image is null && old.Image is null)
            ? null
            : Create(string.Empty, showLessRelevant: false, ScopeFilter(), image, filters: old.Filters));

    /// <summary>
    /// Starts a "find similar" search for an item (E09-S03), replacing the text or image search.
    /// </summary>
    /// <param name="similarTo">The item to find similar items to.</param>
    public void SetSimilarTo(SimilarSource similarTo)
    {
        ArgumentNullException.ThrowIfNull(similarTo);
        Replace(old => old.Similar == similarTo ? null : Create(string.Empty, showLessRelevant: false, ScopeFilter(), similar: similarTo, filters: old.Filters));
    }

    /// <summary>
    /// Leaves the "find similar" search and goes back to browsing; does nothing when it isn't one.
    /// </summary>
    public void ClearSimilar() => Replace(old => old.Similar is null ? null : Create(string.Empty, showLessRelevant: false, ScopeFilter(), filters: old.Filters));

    /// <summary>
    /// Sets the active filters (E10-S01-T03) and runs the current search, or browsing, again with them. The text,
    /// reference image or "find similar" item stays; the source of a "find similar" search is still pinned first.
    /// </summary>
    /// <param name="filters">The filters; <see cref="FilterState.Empty"/> clears them.</param>
    public void SetFilters(FilterState filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        Replace(old => old.Filters.Equals(filters) ? null : Create(old.Text, old.ShowLessRelevant, ScopeFilter(), old.Image, old.Similar, filters));
    }

    /// <summary>
    /// Changes the order of the results shown now (E10-S04-T02): <see cref="SearchSort"/> while searching,
    /// <see cref="BrowseSort"/> while browsing, where relevance means nothing and is ignored.
    /// </summary>
    /// <param name="sort">The order.</param>
    public void SetSort(SortChoice sort)
    {
        ArgumentNullException.ThrowIfNull(sort);
        Replace(old =>
        {
            if (old.IsSearchContext)
            {
                if (searchSort == sort)
                {
                    return null;
                }

                searchSort = sort;
            }
            else
            {
                if (browseSort == sort || sort.Mode == SortMode.Relevance)
                {
                    return null;
                }

                browseSort = sort;
            }

            return Create(old.Text, old.ShowLessRelevant, ScopeFilter(), old.Image, old.Similar, old.Filters);
        });
    }

    /// <summary>
    /// Sets both orders at once, e.g. the ones remembered from the last session, and shows the results again in the one
    /// that applies. A relevance order for browsing is ignored.
    /// </summary>
    /// <param name="search">The order while searching.</param>
    /// <param name="browse">The order while browsing.</param>
    public void SetSorts(SortChoice search, SortChoice browse)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(browse);
        Replace(old =>
        {
            var browseOrder = browse.Mode == SortMode.Relevance ? browseSort : browse;
            if (searchSort == search && browseSort == browseOrder)
            {
                return null;
            }

            searchSort = search;
            browseSort = browseOrder;
            return Create(old.Text, old.ShowLessRelevant, ScopeFilter(), old.Image, old.Similar, old.Filters);
        });
    }

    /// <summary>
    /// Runs the current "find similar" search again, e.g. once its item has been analyzed (E09-S03-T03).
    /// </summary>
    public void RefreshSimilar() => Replace(old => old.Similar is { } similar ? Create(string.Empty, old.ShowLessRelevant, old.Filter, similar: similar, filters: old.Filters) : null);

    /// <summary>
    /// Runs the current search, or browsing, again with the same text, picture, "find similar" item, filters and
    /// order (E16-S02, F5), so the results show changes made since they were listed.
    /// </summary>
    public void Refresh()
    {
        pages.Clear();
        Replace(old => Create(old.Text, old.ShowLessRelevant, ScopeFilter(), old.Image, old.Similar, old.Filters));
    }

    /// <summary>
    /// Includes or leaves out the less relevant semantic matches of the current search.
    /// </summary>
    /// <param name="show">Whether to include them.</param>
    public void SetShowLessRelevant(bool show) =>
        Replace(old => old.IsSearch && old.ShowLessRelevant != show ? Create(old.Text, show, old.Filter, old.Image, filters: old.Filters) : null);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        var search = current;
        if (!search.IsSearch)
        {
            SetSummary(null);
            return await browse.CountAsync(cancellationToken).ConfigureAwait(false);
        }

        return await RunAsync(
            search,
            0,
            cancellationToken,
            async token =>
            {
                var results = await SendAsync(search.Query(0, 1, includeFields: false), token).ConfigureAwait(false);
                if (ReferenceEquals(current, search))
                {
                    // Plain browsing in another order is a search only underneath: it has no notice.
                    SetSummary(search.HasSummary
                        ? new SearchSummary(search.Text, results.Mode, results.Semantic, results.TotalCount, results.LessRelevantCount, search.ShowLessRelevant, search.Similar, results.SkippedCount)
                        : null);
                }

                return results.TotalCount + search.Pinned;
            }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaItemSummary>> GetPageAsync(int offset, int limit, CancellationToken cancellationToken)
    {
        var search = current;
        if (!search.IsSearch)
        {
            return await browse.GetPageAsync(offset, limit, cancellationToken).ConfigureAwait(false);
        }

        return await RunAsync<IReadOnlyList<MediaItemSummary>>(
            search,
            [],
            cancellationToken,
            async token =>
            {
                // The source of a "find similar" search is result 0, before the hits.
                var source = offset == 0 ? search.Similar : null;
                var take = Math.Min(limit, SearchQuery.MaxLimit) - (source is null ? 0 : 1);
                var hits = take == 0
                    ? []
                    : (await SendAsync(search.Query(Math.Max(0, offset - search.Pinned), take, includeFields: true), token).ConfigureAwait(false)).Hits;
                var ids = hits.Select(hit => hit.ItemId).ToList();
                if (source is not null)
                {
                    ids.Insert(0, source.ItemId);
                }

                var summaries = (await sender.Send(new GetItemSummariesQuery(ids), token).ConfigureAwait(false))
                    .ToDictionary(summary => summary.Id);
                var page = new List<MediaItemSummary>(ids.Count);
                if (source is not null && summaries.TryGetValue(source.ItemId, out var pinned))
                {
                    page.Add(pinned);
                    search.Matches[pinned.Id] = new ResultMatch(SearchFields.None, IsSemantic: false, IsLessRelevant: false, IsSource: true);
                }

                foreach (var hit in hits)
                {
                    if (summaries.TryGetValue(hit.ItemId, out var summary))
                    {
                        page.Add(summary);
                        search.Matches[hit.ItemId] = new ResultMatch(hit.MatchedFields, hit.Similarity is not null, hit.IsLessRelevant);
                    }
                }

                return page;
            }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> PositionOfAsync(Id<MediaItem> itemId, CancellationToken cancellationToken)
    {
        var search = current;
        if (!search.IsSearch)
        {
            return await browse.PositionOfAsync(itemId, cancellationToken).ConfigureAwait(false);
        }

        var ids = await AllIdsAsync(search, cancellationToken).ConfigureAwait(false);
        return Math.Max(0, ids.IndexOf(itemId));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Id<MediaItem>>> GetIdsAsync(int offset, int limit, Id<MediaItem>? newest, CancellationToken cancellationToken)
    {
        var search = current;
        if (!search.IsSearch)
        {
            return await browse.GetIdsAsync(offset, limit, newest, cancellationToken).ConfigureAwait(false);
        }

        // The results added up to the boundary are the ones whose UUIDv7 isn't newer, in any order (E10-S04-T02).
        var ids = await AllIdsAsync(search, cancellationToken).ConfigureAwait(false);
        return [.. (newest is { } boundary ? ids.Where(id => ItemSelection.IsUpTo(id, boundary)) : ids).Skip(offset).Take(limit)];
    }

    /// <inheritdoc />
    public async Task<Id<MediaItem>?> GetNewestIdAsync(CancellationToken cancellationToken)
    {
        var search = current;
        var ids = await browse.GetIdsAsync(0, 1, null, cancellationToken).ConfigureAwait(false);
        Id<MediaItem>? newest = ids.Count == 0 ? null : ids[0];
        if (!search.IsSearch)
        {
            return newest;
        }

        // Every result is in the browse scope, whose newest item is first in the item list; the pinned source of a
        // "find similar" search may come from outside it.
        if (search.Similar is { } similar && (newest is not { } scopeNewest || !ItemSelection.IsUpTo(similar.ItemId, scopeNewest)))
        {
            newest = similar.ItemId;
        }

        return newest;
    }

    /// <inheritdoc />
    public async Task<int> CountNewerAsync(Id<MediaItem> newest, CancellationToken cancellationToken)
    {
        var search = current;
        if (!search.IsSearch)
        {
            return await browse.PositionOfAsync(newest, cancellationToken).ConfigureAwait(false);
        }

        var ids = await AllIdsAsync(search, cancellationToken).ConfigureAwait(false);
        return ids.Count(id => !ItemSelection.IsUpTo(id, newest));
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<Id<MediaItem>>> GetExistingIdsAsync(IReadOnlyCollection<Id<MediaItem>> ids, CancellationToken cancellationToken)
    {
        var search = current;
        if (!search.IsSearch)
        {
            return await browse.GetExistingIdsAsync(ids, cancellationToken).ConfigureAwait(false);
        }

        var all = (await AllIdsAsync(search, cancellationToken).ConfigureAwait(false)).ToHashSet();
        return ids.Where(all.Contains).ToHashSet();
    }

    /// <inheritdoc />
    public ResultMatch? GetMatch(Id<MediaItem> itemId) => current.Matches.GetValueOrDefault(itemId);

    // Every hit's ID in result order: the grid asks for them to select ranges and everything, and to find an item.
    // The list is read once per search and shared; it is cancelled only when the search is replaced.
    private async Task<List<Id<MediaItem>>> AllIdsAsync(ActiveSearch search, CancellationToken cancellationToken)
    {
        Task<List<Id<MediaItem>>> task;
        lock (search)
        {
            if (search.AllIds is { IsFaulted: true } or { IsCanceled: true })
            {
                search.AllIds = null;
            }

            task = search.AllIds ??= RunAsync(
                search,
                [],
                CancellationToken.None,
                async token =>
                {
                    var ids = new List<Id<MediaItem>>();
                    if (search.Similar is { } similar)
                    {
                        ids.Add(similar.ItemId);
                    }

                    while (true)
                    {
                        var page = await SendAsync(search.Query(ids.Count - search.Pinned, IdPageSize, includeFields: false), token).ConfigureAwait(false);
                        ids.AddRange(page.Hits.Select(hit => hit.ItemId));
                        if (page.Hits.Count < IdPageSize || ids.Count - search.Pinned >= page.TotalCount)
                        {
                            return ids;
                        }
                    }
                });
        }

        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    // The first page of a text search comes from the cache when the same search ran lately (E09-S02-T02); the other
    // searches always run.
    private async Task<MediaSearchResults> SendAsync(IRequest<MediaSearchResults> request, CancellationToken cancellationToken)
    {
        if (request is not SearchMediaQuery query || !SearchPageCache.CanCache(query))
        {
            return await sender.Send(request, cancellationToken).ConfigureAwait(false);
        }

        if (pages.TryGet(query, out var cached))
        {
            return cached;
        }

        var readAt = pages.Version;
        var results = await sender.Send(query, cancellationToken).ConfigureAwait(false);
        if (SearchPageCache.CanCache(results))
        {
            pages.Set(query, results, readAt);
        }

        return results;
    }

    // A superseded search ends quietly with the fallback, even when its answer was already complete: its caller is about
    // to refresh for the new one.
    private static async Task<T> RunAsync<T>(ActiveSearch search, T fallback, CancellationToken cancellationToken, Func<CancellationToken, Task<T>> run)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, search.Cancellation.Token);
        try
        {
            var result = await run(linked.Token).ConfigureAwait(false);

            // A search replaced while its answer was on the way must not show it: results never arrive out of order.
            return !cancellationToken.IsCancellationRequested && search.Cancellation.IsCancellationRequested ? fallback : result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && search.Cancellation.IsCancellationRequested)
        {
            return fallback;
        }
    }

    // Search covers the items of the selected browse scope; Excluded items never pass the search filter (D048).
    private SearchFilter ScopeFilter()
    {
        var filter = browse.CurrentFilter;
        return new SearchFilter { FolderId = filter.FolderId, Subfolder = filter.Subfolder, ImportedSince = filter.ImportedSince, IsFavorite = filter.IsFavorite };
    }

    private ActiveSearch Create(
        string text,
        bool showLessRelevant,
        SearchFilter filter,
        ReferenceImage? image = null,
        SimilarSource? similar = null,
        FilterState? filters = null)
    {
        var state = filters ?? FilterState.Empty;
        var hasConditions = filterRegistry is null ? !state.IsEmpty : state.ToConditions(filterRegistry).Count > 0;
        var isSearchContext = text.Length > 0 || image is not null || similar is not null;
        return new ActiveSearch(text, showLessRelevant, filter, image, similar, state, hasConditions, isSearchContext ? searchSort : browseSort);
    }

    private void Restart()
    {
        ActiveSearch old;
        lock (syncRoot)
        {
            old = current;
            if (!old.IsSearch)
            {
                return;
            }

            current = Create(old.Text, old.ShowLessRelevant, ScopeFilter(), old.Image, old.Similar, old.Filters);
        }

        old.Cancellation.Cancel();
    }

    private void Replace(Func<ActiveSearch, ActiveSearch?> change)
    {
        ActiveSearch old;
        lock (syncRoot)
        {
            old = current;
            if (change(old) is not { } next)
            {
                return;
            }

            current = next;
        }

        old.Cancellation.Cancel();
        if (!current.IsSearch)
        {
            SetSummary(null);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SetSummary(SearchSummary? summary)
    {
        if (Equals(Summary, summary))
        {
            return;
        }

        Summary = summary;
        SummaryChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class ActiveSearch(
        string text,
        bool showLessRelevant,
        SearchFilter filter,
        ReferenceImage? image = null,
        SimilarSource? similar = null,
        FilterState? filters = null,
        bool hasConditions = false,
        SortChoice? sort = null)
    {
        // The order of the M1 browse results (D177), which the library's item list gives without the search index.
        private static readonly SortChoice RepositoryOrder = new(SortMode.ImportDate, Descending: true);

        public string Text { get; } = text;

        public SearchFilter Filter { get; } = filter;

        public bool ShowLessRelevant { get; } = showLessRelevant;

        public ReferenceImage? Image { get; } = image;

        public SimilarSource? Similar { get; } = similar;

        public FilterState Filters { get; } = filters ?? FilterState.Empty;

        public SortChoice Sort { get; } = sort ?? SortChoice.BrowseDefault;

        public bool IsSearchContext => Text.Length > 0 || Image is not null || Similar is not null;

        // Filters alone make a search too: browsing through the search index, which applies them. A state whose filters
        // are all unregistered or unavailable filters nothing, so it browses plainly.
        public bool HasSummary => IsSearchContext || hasConditions;

        // Browsing in any order but the item list's goes through the search index, which has the sort keys (E10-S04).
        public bool IsSearch => HasSummary || Sort != RepositoryOrder;

        // How many results come before the hits: the source of a "find similar" search.
        public int Pinned => Similar is null ? 0 : 1;

        public CancellationTokenSource Cancellation { get; } = new();

        public Task<List<Id<MediaItem>>>? AllIds { get; set; }

        public ConcurrentDictionary<Id<MediaItem>, ResultMatch> Matches { get; } = new();

        public IRequest<MediaSearchResults> Query(int offset, int limit, bool includeFields) => Similar is { } similar
            ? new FindSimilarQuery(similar.ItemId) { Filter = Filter, Filters = Filters, Sort = Sort.Mode, ReverseSort = Sort.ReverseSort, Offset = offset, Limit = limit }
            : new SearchMediaQuery
            {
                Text = Text,
                ReferenceImage = Image,
                Filter = Filter,
                Filters = Filters,

                Sort = Sort.Mode,
                ReverseSort = Sort.ReverseSort,
                Offset = offset,
                Limit = limit,
                IncludeMatchedFields = includeFields,
                ShowLessRelevant = ShowLessRelevant,
            };
    }
}
