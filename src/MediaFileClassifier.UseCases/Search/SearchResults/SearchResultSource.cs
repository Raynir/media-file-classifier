using System.Collections.Concurrent;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.UseCases.Browsing;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
using MediaFileClassifier.UseCases.Search.SearchMedia;
using MediatR;

namespace MediaFileClassifier.UseCases.Search.SearchResults;

/// <summary>
/// What the search showed last, for the notice above the results (E09-S01-T05).
/// </summary>
/// <param name="Text">The search text.</param>
/// <param name="Mode">Which searches produced the results.</param>
/// <param name="Semantic">Whether the semantic search ran, and why not if it didn't.</param>
/// <param name="TotalCount">The number of results shown.</param>
/// <param name="LessRelevantCount">The N of "Show N less relevant results".</param>
/// <param name="ShowLessRelevant">Whether the less relevant results are included.</param>
public sealed record SearchSummary(
    string Text,
    SearchMode Mode,
    SemanticSearchStatus Semantic,
    int TotalCount,
    int LessRelevantCount,
    bool ShowLessRelevant);

/// <summary>
/// The result source of the media browser from M2 (E09-S01-T05): the unified text search while the search text has
/// something to look for, and the browse results of <see cref="LibraryItemsSource"/> while it's blank. Changing the
/// text, the less relevant option or the browse scope starts a new search and cancels the one still running. A search
/// covers the selected scope (folder, subfolder, recently imported); the filters (E10) will add to it.
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
    /// Gets a value indicating whether the less relevant semantic matches are included.
    /// </summary>
    public bool ShowLessRelevant => current.ShowLessRelevant;

    /// <summary>
    /// Gets what the search showed last, or <see langword="null"/> while browsing.
    /// </summary>
    public SearchSummary? Summary { get; private set; }

    /// <summary>
    /// Starts a search for the text, or goes back to browsing when it's blank. The text is searched as it is
    /// (<see cref="SearchText.Normalise"/> runs in the search); the less relevant option resets.
    /// </summary>
    /// <param name="text">What the user typed.</param>
    public void SetText(string? text)
    {
        var value = string.IsNullOrWhiteSpace(text) ? string.Empty : text;
        Replace(old => old.Text == value ? null : new ActiveSearch(value, showLessRelevant: false, ScopeFilter()));
    }

    /// <summary>
    /// Includes or leaves out the less relevant semantic matches of the current search.
    /// </summary>
    /// <param name="show">Whether to include them.</param>
    public void SetShowLessRelevant(bool show) =>
        Replace(old => old.IsSearch && old.ShowLessRelevant != show ? new ActiveSearch(old.Text, show, old.Filter) : null);

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
                    SetSummary(new SearchSummary(search.Text, results.Mode, results.Semantic, results.TotalCount, results.LessRelevantCount, search.ShowLessRelevant));
                }

                return results.TotalCount;
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
                var results = await sender.Send(search.Query(offset, Math.Min(limit, SearchQuery.MaxLimit), includeFields: true), token).ConfigureAwait(false);
                var summaries = (await sender.Send(new GetItemSummariesQuery([.. results.Hits.Select(hit => hit.ItemId)]), token).ConfigureAwait(false))
                    .ToDictionary(summary => summary.Id);
                var page = new List<MediaItemSummary>(results.Hits.Count);
                foreach (var hit in results.Hits)
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
                    while (true)
                    {
                        var page = await sender.Send(search.Query(ids.Count, IdPageSize, includeFields: false), token).ConfigureAwait(false);
                        ids.AddRange(page.Hits.Select(hit => hit.ItemId));
                        if (page.Hits.Count < IdPageSize || ids.Count >= page.TotalCount)
                        {
                            return ids;
                        }
                    }
                });
        }

        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    // A superseded search ends quietly with the fallback: its caller is about to refresh for the new one.
    private static async Task<T> RunAsync<T>(ActiveSearch search, T fallback, CancellationToken cancellationToken, Func<CancellationToken, Task<T>> run)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, search.Cancellation.Token);
        try
        {
            return await run(linked.Token).ConfigureAwait(false);
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

            current = new ActiveSearch(old.Text, old.ShowLessRelevant, ScopeFilter());
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

    private sealed class ActiveSearch(string text, bool showLessRelevant, SearchFilter filter)
    {
        public static ActiveSearch None { get; } = new(string.Empty, showLessRelevant: false, SearchFilter.None);

        public string Text { get; } = text;

        public SearchFilter Filter { get; } = filter;

        public bool ShowLessRelevant { get; } = showLessRelevant;

        public bool IsSearch => Text.Length > 0;

        public CancellationTokenSource Cancellation { get; } = new();

        public Task<List<Id<MediaItem>>>? AllIds { get; set; }

        public ConcurrentDictionary<Id<MediaItem>, ResultMatch> Matches { get; } = new();

        public SearchMediaQuery Query(int offset, int limit, bool includeFields) => new()
        {
            Text = Text,
            Filter = Filter,
            Offset = offset,
            Limit = limit,
            IncludeMatchedFields = includeFields,
            ShowLessRelevant = ShowLessRelevant,
        };
    }
}
