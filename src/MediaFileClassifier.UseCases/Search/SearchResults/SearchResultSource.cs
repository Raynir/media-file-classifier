using System.Collections.Concurrent;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.UseCases.Browsing;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
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
public sealed record SearchSummary(
    string Text,
    SearchMode Mode,
    SemanticSearchStatus Semantic,
    int TotalCount,
    int LessRelevantCount,
    bool ShowLessRelevant,
    SimilarSource? SimilarTo = null);

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
/// subfolder, recently imported); the filters (E10) will add to it.
/// "Find similar" (E09-S03) is the third kind of search: <see cref="SetSimilarTo"/> lists the items like one item, with
/// that item first, marked Source; it doesn't count in the summary's total (D053). Text typed or a reference image
/// replaces it, and it replaces a reference image.
/// </summary>
public sealed class SearchResultSource : IMediaResultSource, IResultAnnotations
{
    private const int IdPageSize = SearchQuery.MaxLimit;

    private readonly LibraryItemsSource browse;
    private readonly ISender sender;
    private readonly Lock syncRoot = new();
    private ActiveSearch current = ActiveSearch.None;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchResultSource"/> class.
    /// </summary>
    /// <param name="browse">The results while there's no search text.</param>
    /// <param name="sender">Sends the queries.</param>
    public SearchResultSource(LibraryItemsSource browse, ISender sender)
    {
        this.browse = browse;
        this.sender = sender;
        browse.Changed += (_, _) =>
        {
            // The scope or the items changed: a search starts over inside the new scope, on the current items.
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
            : new ActiveSearch(value, showLessRelevant: false, ScopeFilter()));
    }

    /// <summary>
    /// Starts an image-to-image search with the picture (E09-S04), replacing the text or a "find similar" search.
    /// <see langword="null"/> removes the picture and goes back to browsing; it does nothing to another search.
    /// </summary>
    /// <param name="image">The picture, from <see cref="LoadReferenceImageQuery"/>.</param>
    public void SetReferenceImage(ReferenceImage? image) =>
        Replace(old => ReferenceEquals(old.Image, image) || (image is null && old.Image is null)
            ? null
            : new ActiveSearch(string.Empty, showLessRelevant: false, ScopeFilter(), image));

    /// <summary>
    /// Starts a "find similar" search for an item (E09-S03), replacing the text or image search.
    /// </summary>
    /// <param name="similarTo">The item to find similar items to.</param>
    public void SetSimilarTo(SimilarSource similarTo)
    {
        ArgumentNullException.ThrowIfNull(similarTo);
        Replace(old => old.Similar == similarTo ? null : new ActiveSearch(string.Empty, showLessRelevant: false, ScopeFilter(), similar: similarTo));
    }

    /// <summary>
    /// Leaves the "find similar" search and goes back to browsing; does nothing when it isn't one.
    /// </summary>
    public void ClearSimilar() => Replace(old => old.Similar is null ? null : ActiveSearch.None);

    /// <summary>
    /// Runs the current "find similar" search again, e.g. once its item has been analyzed (E09-S03-T03).
    /// </summary>
    public void RefreshSimilar() => Replace(old => old.Similar is { } similar ? new ActiveSearch(string.Empty, old.ShowLessRelevant, old.Filter, similar: similar) : null);

    /// <summary>
    /// Includes or leaves out the less relevant semantic matches of the current search.
    /// </summary>
    /// <param name="show">Whether to include them.</param>
    public void SetShowLessRelevant(bool show) =>
        Replace(old => old.IsSearch && old.ShowLessRelevant != show ? new ActiveSearch(old.Text, show, old.Filter, old.Image) : null);

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
                var results = await sender.Send(search.Query(0, 1, includeFields: false), token).ConfigureAwait(false);
                if (ReferenceEquals(current, search))
                {
                    SetSummary(new SearchSummary(search.Text, results.Mode, results.Semantic, results.TotalCount, results.LessRelevantCount, search.ShowLessRelevant, search.Similar));
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
                    : (await sender.Send(search.Query(Math.Max(0, offset - search.Pinned), take, includeFields: true), token).ConfigureAwait(false)).Hits;
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

        // In a search the order is the relevance order, so "from this result on" is from its position.
        var ids = await AllIdsAsync(search, cancellationToken).ConfigureAwait(false);
        var start = newest is { } from ? Math.Max(0, ids.IndexOf(from)) : 0;
        return [.. ids.Skip(start + offset).Take(limit)];
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
                        var page = await sender.Send(search.Query(ids.Count - search.Pinned, IdPageSize, includeFields: false), token).ConfigureAwait(false);
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
        return new SearchFilter { FolderId = filter.FolderId, Subfolder = filter.Subfolder, ImportedSince = filter.ImportedSince };
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

            current = new ActiveSearch(old.Text, old.ShowLessRelevant, ScopeFilter(), old.Image, old.Similar);
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

    private sealed class ActiveSearch(string text, bool showLessRelevant, SearchFilter filter, ReferenceImage? image = null, SimilarSource? similar = null)
    {
        public static ActiveSearch None { get; } = new(string.Empty, showLessRelevant: false, SearchFilter.None);

        public string Text { get; } = text;

        public SearchFilter Filter { get; } = filter;

        public bool ShowLessRelevant { get; } = showLessRelevant;

        public ReferenceImage? Image { get; } = image;

        public SimilarSource? Similar { get; } = similar;

        public bool IsSearch => Text.Length > 0 || Image is not null || Similar is not null;

        // How many results come before the hits: the source of a "find similar" search.
        public int Pinned => Similar is null ? 0 : 1;

        public CancellationTokenSource Cancellation { get; } = new();

        public Task<List<Id<MediaItem>>>? AllIds { get; set; }

        public ConcurrentDictionary<Id<MediaItem>, ResultMatch> Matches { get; } = new();

        public IRequest<MediaSearchResults> Query(int offset, int limit, bool includeFields) => Similar is { } similar
            ? new FindSimilarQuery(similar.ItemId) { Filter = Filter, Offset = offset, Limit = limit }
            : new SearchMediaQuery
            {
                Text = Text,
                ReferenceImage = Image,
                Filter = Filter,
                Offset = offset,
                Limit = limit,
                IncludeMatchedFields = includeFields,
                ShowLessRelevant = ShowLessRelevant,
            };
    }
}
