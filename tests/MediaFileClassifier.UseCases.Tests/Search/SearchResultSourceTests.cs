using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.UseCases.Browsing;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
using MediaFileClassifier.UseCases.Search.Filters;
using MediaFileClassifier.UseCases.Tests.Search.Filters;
using MediaFileClassifier.UseCases.Search.FindSimilar;
using MediaFileClassifier.UseCases.Search.SearchMedia;
using MediaFileClassifier.UseCases.Search.SearchResults;
using MediaFileClassifier.UseCases.Tests.Libraries;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.UseCases.Tests.Search;

/// <summary>
/// Tests for <see cref="SearchResultSource"/> (E09-S01-T05): the browser's results while searching and browsing,
/// with the unified search replaced by a stub that returns the hits the test sets.
/// </summary>
public sealed class SearchResultSourceTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeLibrarySession session = new();
    private readonly ServiceProvider services;
    private readonly StubSender sender;
    private readonly LibraryItemsSource browse;
    private readonly SearchResultSource source;
    private FakeOpenLibrary? library;

    public SearchResultSourceTests()
    {
        services = new ServiceCollection().AddSingleton<ILibrarySession>(session).AddUseCases().BuildServiceProvider();
        sender = new StubSender(services.GetRequiredService<ISender>());
        browse = services.GetRequiredService<LibraryItemsSource>();
        source = new SearchResultSource(browse, sender);

        // Most tests browse in the M1 order (D177), which the item list gives without the search.
        source.SetSort(ItemListOrder);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static SortChoice ItemListOrder { get; } = new(SortMode.ImportDate, Descending: true);

    private static FilterState Videos { get; } = FilterState.Empty.Set("media-type", new MultiSelectValue(["Video"]));

    public async ValueTask DisposeAsync()
    {
        if (library is not null)
        {
            await library.DisposeAsync();
        }

        await services.DisposeAsync();
    }

    [Fact]
    public async Task Browsing_ListsTheLibraryAndHasNoSummary()
    {
        await OpenLibraryAsync(3);

        Assert.Equal(3, await source.CountAsync(Token));
        Assert.Equal(3, (await source.GetPageAsync(0, 10, Token)).Count);
        Assert.Null(source.Summary);
        Assert.Equal(0, sender.SearchCalls);
    }

    [Fact]
    public async Task Browsing_ByDefault_SortsByDateTaken_ThroughTheSearch_WithoutASummary()
    {
        await OpenLibraryAsync(3);
        var items = library!.ItemList.Items;
        var fresh = new SearchResultSource(browse, sender);
        sender.Hits = [new SearchHit(items[2].Id, 0, null, null, SearchFields.None), new SearchHit(items[0].Id, 0, null, null, SearchFields.None)];

        var count = await fresh.CountAsync(Token);

        Assert.Equal(SortChoice.BrowseDefault, fresh.Sort);
        Assert.False(fresh.IsSearchContext);
        Assert.Equal(2, count);
        Assert.Equal(SortMode.Date, sender.LastQuery!.Sort);
        Assert.False(sender.LastQuery.ReverseSort);
        Assert.True(string.IsNullOrEmpty(sender.LastQuery.Text));
        Assert.Null(fresh.Summary);
        Assert.Equal([items[2].Id, items[0].Id], (await fresh.GetPageAsync(0, 10, Token)).Select(item => item.Id));
    }

    [Fact]
    public async Task Sort_IsRememberedSeparately_ForSearchingAndBrowsing()
    {
        await OpenLibraryAsync(2);
        var changed = 0;
        source.Changed += (_, _) => changed++;

        source.SetSort(new SortChoice(SortMode.Name, Descending: false));
        await source.CountAsync(Token);
        Assert.Equal((SortMode.Name, false), (sender.LastQuery!.Sort, sender.LastQuery.ReverseSort));

        source.SetText("car");
        Assert.True(source.IsSearchContext);
        Assert.Equal(SortChoice.SearchDefault, source.Sort);
        await source.CountAsync(Token);
        Assert.Equal(SortMode.Relevance, sender.LastQuery.Sort);

        source.SetSort(new SortChoice(SortMode.Size, Descending: false));
        await source.CountAsync(Token);
        Assert.Equal((SortMode.Size, true), (sender.LastQuery.Sort, sender.LastQuery.ReverseSort));

        source.SetText(string.Empty);
        await source.CountAsync(Token);
        Assert.Equal((SortMode.Name, false), (sender.LastQuery.Sort, sender.LastQuery.ReverseSort));
        Assert.Equal(new SortChoice(SortMode.Name, false), source.BrowseSort);
        Assert.Equal(new SortChoice(SortMode.Size, false), source.SearchSort);
        Assert.Equal(4, changed);
    }

    [Fact]
    public void Sort_RelevanceWhileBrowsing_AndTheSameOrderAgain_ChangeNothing()
    {
        var changed = 0;
        source.Changed += (_, _) => changed++;

        source.SetSort(SortChoice.SearchDefault);
        source.SetSort(ItemListOrder);
        source.SetSorts(SortChoice.SearchDefault, SortChoice.SearchDefault);

        Assert.Equal(0, changed);
        Assert.Equal(ItemListOrder, source.BrowseSort);
    }

    [Fact]
    public async Task SelectAll_InAnotherOrder_CoversTheResultsUpToTheNewestItem_WhateverTheirPosition()
    {
        await OpenLibraryAsync(4);
        var byAge = library!.ItemList.Items.Select(item => item.Id).ToList();
        var fresh = new SearchResultSource(browse, sender);

        // Date order puts the newest import second and the oldest first.
        sender.Hits = [.. new[] { byAge[0], byAge[3], byAge[1], byAge[2] }.Select(id => new SearchHit(id, 0, null, null, SearchFields.None))];
        IMediaResultSource results = fresh;

        var newest = await results.GetNewestIdAsync(Token);

        Assert.Equal(byAge[3], newest);
        Assert.Equal(4, (await results.GetIdsAsync(0, int.MaxValue, newest, Token)).Count);
        Assert.Equal([byAge[0], byAge[1]], await results.GetIdsAsync(0, int.MaxValue, byAge[1], Token));
        Assert.Equal([byAge[1]], await results.GetIdsAsync(1, 5, byAge[1], Token));
        Assert.Equal(2, await results.CountNewerAsync(byAge[1], Token));
        Assert.Equal(0, await results.CountNewerAsync(byAge[3], Token));
    }

    [Fact]
    public async Task Sort_AppliesToFindSimilar()
    {
        await OpenLibraryAsync(2);
        var items = library!.ItemList.Items;
        source.SetSimilarTo(new SimilarSource(items[0].Id, "IMG_0.jpg"));
        source.SetSort(new SortChoice(SortMode.Date, Descending: false));

        await source.CountAsync(Token);

        Assert.Equal((SortMode.Date, true), (sender.LastSimilar!.Sort, sender.LastSimilar.ReverseSort));
    }

    [Fact]
    public async Task Filters_WithNoText_BrowseThroughTheSearch_NewestImportFirst()
    {
        await OpenLibraryAsync(3);
        var items = library!.ItemList.Items;
        sender.Hits = [new SearchHit(items[1].Id, 0, null, null, SearchFields.None)];
        var changed = 0;
        source.Changed += (_, _) => changed++;

        source.SetFilters(Videos);
        var count = await source.CountAsync(Token);

        Assert.Equal(1, changed);
        Assert.Equal(1, count);
        Assert.Equal(Videos, source.Filters);
        Assert.Equal(Videos, sender.LastQuery!.Filters);
        Assert.Equal(SortMode.ImportDate, sender.LastQuery.Sort);
        Assert.True(string.IsNullOrEmpty(sender.LastQuery.Text));
        Assert.Equal([items[1].Id], (await source.GetPageAsync(0, 10, Token)).Select(item => item.Id));
    }

    [Fact]
    public async Task Filters_ApplyToTextSearch_AndStayWhenTheTextChanges()
    {
        await OpenLibraryAsync(2);
        source.SetFilters(Videos);
        source.SetText("car");
        await source.CountAsync(Token);

        Assert.Equal(Videos, sender.LastQuery!.Filters);
        Assert.Equal(SortMode.Relevance, sender.LastQuery.Sort);

        source.SetText(string.Empty);
        await source.CountAsync(Token);

        Assert.Equal(Videos, source.Filters);
        Assert.Equal(Videos, sender.LastQuery.Filters);
    }

    [Fact]
    public async Task Filters_ApplyToFindSimilar_AndStayWhenItIsCleared()
    {
        await OpenLibraryAsync(2);
        var items = library!.ItemList.Items;
        source.SetFilters(Videos);
        source.SetSimilarTo(new SimilarSource(items[0].Id, "IMG_0.jpg"));
        await source.CountAsync(Token);

        Assert.Equal(Videos, sender.LastSimilar!.Filters);

        source.ClearSimilar();

        Assert.Null(source.SimilarTo);
        Assert.Equal(Videos, source.Filters);
    }

    [Fact]
    public async Task FiltersThatAreUnavailable_BrowsePlainly_UntilTheyBecomeAvailable()
    {
        await OpenLibraryAsync(3);
        var filter = new TestFilter<BooleanValue>("toggled", FilterValueKind.Boolean);
        filter.SetAvailable(false);
        var registered = new SearchResultSource(browse, sender, new FilterRegistry([filter]));
        registered.SetSort(ItemListOrder);
        var changed = 0;
        registered.Changed += (_, _) => changed++;

        registered.SetFilters(FilterState.Empty.Set("toggled", new BooleanValue(true)));
        Assert.Equal(3, await registered.CountAsync(Token));
        Assert.Equal(0, sender.SearchCalls);

        filter.SetAvailable(true);
        await registered.CountAsync(Token);

        Assert.Equal(2, changed);
        Assert.Equal(1, sender.SearchCalls);
    }

    [Fact]
    public async Task ClearingTheFilters_GoesBackToPlainBrowsing()
    {
        await OpenLibraryAsync(3);
        source.SetFilters(Videos);
        await source.CountAsync(Token);
        var calls = sender.SearchCalls;
        var changed = 0;
        source.Changed += (_, _) => changed++;

        source.SetFilters(FilterState.Empty.Set("media-type", new MultiSelectValue(["Video"])));
        source.SetFilters(FilterState.Empty);

        Assert.Equal(1, changed);
        Assert.Equal(3, await source.CountAsync(Token));
        Assert.Equal(calls, sender.SearchCalls);
        Assert.Null(source.Summary);
    }

    [Fact]
    public async Task Search_PagesTheHitsInRelevanceOrder_AndSaysWhyEachMatched()
    {
        await OpenLibraryAsync(4);
        var items = library!.ItemList.Items;
        sender.Hits =
        [
            new SearchHit(items[2].Id, 0.9, 1, 0.4f, SearchFields.Tags | SearchFields.OcrText),
            new SearchHit(items[0].Id, 0.5, null, 0.2f, SearchFields.None),
            new SearchHit(items[3].Id, 0.1, null, 0.05f, SearchFields.None) { IsLessRelevant = true },
        ];
        source.SetText("sign");

        var page = await source.GetPageAsync(0, 10, Token);

        Assert.Equal([items[2].Id, items[0].Id, items[3].Id], page.Select(item => item.Id));
        Assert.Equal(new ResultMatch(SearchFields.Tags | SearchFields.OcrText, IsSemantic: true, IsLessRelevant: false), source.GetMatch(items[2].Id));
        Assert.Equal(new ResultMatch(SearchFields.None, IsSemantic: true, IsLessRelevant: false), source.GetMatch(items[0].Id));
        Assert.True(source.GetMatch(items[3].Id)!.IsLessRelevant);
        Assert.Null(source.GetMatch(items[1].Id));
    }

    [Fact]
    public async Task Search_CountAndSummary_FeedTheNotice_AndSkipHitsOfDeletedItems()
    {
        await OpenLibraryAsync(2);
        var items = library!.ItemList.Items;
        sender.Hits = [new SearchHit(Id<MediaItem>.New(), 0.9, 1, null, SearchFields.Title), new SearchHit(items[1].Id, 0.5, 2, null, SearchFields.Title)];
        sender.LessRelevantCount = 7;
        sender.Semantic = SemanticSearchStatus.ModelMissing;
        var summaries = 0;
        source.SummaryChanged += (_, _) => summaries++;
        source.SetText("lighthouse");

        var count = await source.CountAsync(Token);
        var page = await source.GetPageAsync(0, 10, Token);

        Assert.Equal(2, count);
        Assert.Equal(new SearchSummary("lighthouse", SearchMode.Keyword, SemanticSearchStatus.ModelMissing, 2, 7, ShowLessRelevant: false), source.Summary);
        Assert.Equal(1, summaries);
        Assert.Equal([items[1].Id], page.Select(item => item.Id));
    }

    [Fact]
    public async Task ShowLessRelevant_StartsANewSearchWithTheOption_AndBlankTextGoesBackToBrowsing()
    {
        await OpenLibraryAsync(2);
        sender.Hits = [];
        var changes = 0;
        source.Changed += (_, _) => changes++;

        source.SetText("sign");
        source.SetText("sign");
        source.SetShowLessRelevant(true);
        source.SetShowLessRelevant(true);
        await source.CountAsync(Token);

        Assert.Equal(2, changes);
        Assert.True(sender.LastQuery!.ShowLessRelevant);
        Assert.True(source.Summary!.ShowLessRelevant);

        source.SetText("  ");

        Assert.Equal(3, changes);
        Assert.Null(source.Summary);
        Assert.False(source.ShowLessRelevant);
        Assert.Equal(2, await source.CountAsync(Token));
    }

    [Fact]
    public async Task Search_StaysInsideTheSelectedScope_AndRestartsWhenTheScopeChanges()
    {
        await OpenLibraryAsync(2);
        var folder = library!.FolderList.Folders[0];
        source.SetText("sign");
        await source.CountAsync(Token);
        Assert.Null(sender.LastQuery!.Filter.FolderId);
        var changes = 0;
        source.Changed += (_, _) => changes++;

        browse.Scope = new FolderScope(folder.Id, "trip/day1");
        await source.CountAsync(Token);

        Assert.Equal(1, changes);
        Assert.Equal(folder.Id, sender.LastQuery.Filter.FolderId);
        Assert.Equal("trip/day1", sender.LastQuery.Filter.Subfolder);
        Assert.Equal("sign", source.Text);

        browse.Scope = BrowseScope.RecentlyImported;
        await source.CountAsync(Token);

        Assert.NotNull(sender.LastQuery.Filter.ImportedSince);
        Assert.Null(sender.LastQuery.Filter.FolderId);
    }

    [Fact]
    public async Task ANewTextCancelsTheSearchStillRunning_WithoutAnError()
    {
        await OpenLibraryAsync(1);
        sender.Block = new TaskCompletionSource();
        source.SetText("first");
        var running = source.GetPageAsync(0, 10, Token);
        await sender.Started.Task.WaitAsync(Token);

        source.SetText("second");
        sender.Block.SetResult();

        Assert.Empty(await running);
    }

    [Fact]
    public async Task ASearchReplacedWhileItsAnswerIsComplete_ShowsNothingOfIt()
    {
        await OpenLibraryAsync(2);
        var items = library!.ItemList.Items;
        sender.Hits = [new SearchHit(items[0].Id, 1, 1, null, SearchFields.Title)];
        sender.Block = new TaskCompletionSource();
        sender.IgnoreCancellation = true;
        source.SetText("first");
        var page = source.GetPageAsync(0, 10, Token);
        var count = source.CountAsync(Token);
        await sender.Started.Task.WaitAsync(Token);

        source.SetText("second");
        sender.Block.SetResult();

        Assert.Empty(await page);
        Assert.Equal(0, await count);
        Assert.Null(source.Summary);
        Assert.Null(source.GetMatch(items[0].Id));
    }

    [Fact]
    public async Task TypingQuickly_OnlyTheLastTextShowsResults_AndEveryEarlierSearchEndsQuietly()
    {
        await OpenLibraryAsync(2);
        var items = library!.ItemList.Items;
        sender.Hits = [new SearchHit(items[1].Id, 1, 1, null, SearchFields.Title)];
        sender.Block = new TaskCompletionSource();
        var running = new List<Task<IReadOnlyList<MediaItemSummary>>>();
        foreach (var text in new[] { "b", "bo", "boa", "boat" })
        {
            source.SetText(text);
            running.Add(source.GetPageAsync(0, 10, Token));
        }

        await sender.Started.Task.WaitAsync(Token);
        sender.Block.SetResult();
        var pages = await Task.WhenAll(running);

        Assert.All(pages[..3], earlier => Assert.Empty(earlier));
        Assert.Equal([items[1].Id], pages[3].Select(item => item.Id));
        Assert.Equal("boat", source.Text);
    }

    [Fact]
    public async Task GoingBackToARecentText_ShowsItsFirstPageWithoutSearchingAgain()
    {
        await OpenLibraryAsync(2);
        var items = library!.ItemList.Items;
        sender.Hits = [new SearchHit(items[1].Id, 1, 1, null, SearchFields.Title)];

        source.SetText("boat");
        await source.CountAsync(Token);
        await source.GetPageAsync(0, 10, Token);
        Assert.Equal(2, sender.SearchCalls);

        source.SetText("boa");
        await source.CountAsync(Token);
        await source.GetPageAsync(0, 10, Token);
        Assert.Equal(4, sender.SearchCalls);

        // Back to the first text: the count, the page and the match reasons all come from the cache.
        source.SetText("boat");
        var count = await source.CountAsync(Token);
        var page = await source.GetPageAsync(0, 10, Token);

        Assert.Equal(4, sender.SearchCalls);
        Assert.Equal(1, count);
        Assert.Equal([items[1].Id], page.Select(item => item.Id));
        Assert.Equal(SearchFields.Title, source.GetMatch(items[1].Id)!.Fields);
        Assert.Equal(1, source.Summary!.TotalCount);
    }

    [Fact]
    public async Task TheCache_IsKeyedByTheOptionsAndTheScope_AndForgetsWhenTheScopeOrItemsChange()
    {
        await OpenLibraryAsync(2);
        var folder = library!.FolderList.Folders[0];
        source.SetText("boat");
        await source.CountAsync(Token);
        source.SetShowLessRelevant(true);
        await source.CountAsync(Token);
        Assert.Equal(2, sender.SearchCalls);

        source.SetShowLessRelevant(false);
        await source.CountAsync(Token);
        Assert.Equal(2, sender.SearchCalls);

        browse.Scope = new FolderScope(folder.Id);
        await source.CountAsync(Token);
        browse.Scope = BrowseScope.All;
        await source.CountAsync(Token);
        Assert.Equal(4, sender.SearchCalls);
    }

    [Theory]
    [InlineData(SemanticSearchStatus.IndexNotReady)]
    [InlineData(SemanticSearchStatus.ModelMissing)]
    [InlineData(SemanticSearchStatus.Failed)]
    public async Task AnAnswerWithoutTheSemanticSearchForAReasonThatPasses_IsNotCached(SemanticSearchStatus semantic)
    {
        await OpenLibraryAsync(1);
        sender.Semantic = semantic;
        source.SetText("boat");
        await source.CountAsync(Token);

        source.SetText("boa");
        source.SetText("boat");
        sender.Semantic = SemanticSearchStatus.Used;
        await source.CountAsync(Token);

        Assert.Equal(2, sender.SearchCalls);
        Assert.Equal(SemanticSearchStatus.Used, source.Summary!.Semantic);
    }

    [Fact]
    public async Task OnlyFirstPagesAreCached_AndAnAnswerThatStartedBeforeAChangeIsNotKept()
    {
        await OpenLibraryAsync(1);
        sender.Hits = [];
        source.SetText("boat");
        await source.GetPageAsync(5, 10, Token);
        await source.GetPageAsync(5, 10, Token);
        Assert.Equal(2, sender.SearchCalls);

        sender.Block = new TaskCompletionSource();
        sender.IgnoreCancellation = true;
        var slow = source.CountAsync(Token);
        await sender.Started.Task.WaitAsync(Token);
        browse.Scope = BrowseScope.RecentlyImported;
        sender.Block.SetResult();
        await slow;
        sender.Block = null;
        browse.Scope = BrowseScope.All;
        var calls = sender.SearchCalls;
        await source.CountAsync(Token);

        Assert.Equal(calls + 1, sender.SearchCalls);
    }

    [Fact]
    public async Task Ids_FollowTheSearchOrder_UpToAnItem_AcrossPages()
    {
        await OpenLibraryAsync(5);
        var items = library!.ItemList.Items;
        sender.Hits = [.. new[] { 3, 1, 4, 0 }.Select(index => new SearchHit(items[index].Id, 1, 1, null, SearchFields.Title))];
        source.SetText("sign");

        var all = await source.GetIdsAsync(0, 10, null, Token);
        var upToItem = await source.GetIdsAsync(0, 10, items[1].Id, Token);
        var upToItemSkipped = await source.GetIdsAsync(1, 10, items[1].Id, Token);

        // Items are in the order they were added, so "up to item 1" keeps items 0 and 1, in the search order.
        Assert.Equal([items[3].Id, items[1].Id, items[4].Id, items[0].Id], all);
        Assert.Equal([items[1].Id, items[0].Id], upToItem);
        Assert.Equal([items[0].Id], upToItemSkipped);
        Assert.Equal(2, await source.PositionOfAsync(items[4].Id, Token));
        Assert.Equal(
            new HashSet<Id<MediaItem>> { items[4].Id },
            await source.GetExistingIdsAsync([items[4].Id, items[2].Id], Token));
    }

    [Fact]
    public async Task FindSimilar_ShowsTheSourceFirst_MarkedSource_AndLeavesItOutOfTheTotal()
    {
        await OpenLibraryAsync(4);
        var items = library!.ItemList.Items;
        sender.Hits = [new SearchHit(items[3].Id, 0.95, null, 0.95f, SearchFields.None), new SearchHit(items[1].Id, 0.4, null, 0.4f, SearchFields.None)];
        source.SetSimilarTo(new SimilarSource(items[0].Id, "IMG_0.jpg"));

        var count = await source.CountAsync(Token);
        var page = await source.GetPageAsync(0, 10, Token);
        var rest = await source.GetPageAsync(1, 10, Token);

        Assert.Equal(3, count);
        Assert.Equal([items[0].Id, items[3].Id, items[1].Id], page.Select(item => item.Id));
        Assert.Equal([items[3].Id, items[1].Id], rest.Select(item => item.Id));
        Assert.True(source.GetMatch(items[0].Id)!.IsSource);
        Assert.Equal(new ResultMatch(SearchFields.None, IsSemantic: true, IsLessRelevant: false), source.GetMatch(items[3].Id));
        Assert.Equal(items[0].Id, sender.LastSimilar!.ItemId);
        Assert.Equal(
            new SearchSummary(string.Empty, SearchMode.Similar, SemanticSearchStatus.Used, 2, 0, ShowLessRelevant: false, new SimilarSource(items[0].Id, "IMG_0.jpg")),
            source.Summary);
        Assert.Equal([items[0].Id, items[3].Id, items[1].Id], await source.GetIdsAsync(0, 10, null, Token));
        Assert.Equal(1, await source.PositionOfAsync(items[3].Id, Token));
    }

    [Fact]
    public async Task FindSimilar_UnanalyzedItem_ShowsOnlyTheSource()
    {
        await OpenLibraryAsync(2);
        var items = library!.ItemList.Items;
        sender.Semantic = SemanticSearchStatus.NotAnalyzed;
        source.SetSimilarTo(new SimilarSource(items[1].Id, "IMG_1.jpg"));

        Assert.Equal(1, await source.CountAsync(Token));
        Assert.Equal([items[1].Id], (await source.GetPageAsync(0, 10, Token)).Select(item => item.Id));
        Assert.Equal(SemanticSearchStatus.NotAnalyzed, source.Summary!.Semantic);
        Assert.Equal(0, source.Summary.TotalCount);
    }

    [Fact]
    public async Task FindSimilar_TextReplacesIt_BlankTextKeepsIt_AndClearingGoesBackToBrowsing()
    {
        await OpenLibraryAsync(3);
        var items = library!.ItemList.Items;
        var similar = new SimilarSource(items[0].Id, "IMG_0.jpg");
        var changes = 0;
        source.Changed += (_, _) => changes++;

        source.SetSimilarTo(similar);
        source.SetText(string.Empty);
        Assert.Equal(similar, source.SimilarTo);
        Assert.Equal(1, changes);

        source.SetText("boat");
        Assert.Null(source.SimilarTo);
        Assert.Equal("boat", source.Text);
        Assert.Equal(2, changes);

        source.SetSimilarTo(similar);
        Assert.Equal(string.Empty, source.Text);
        source.ClearSimilar();
        source.ClearSimilar();
        Assert.Null(source.SimilarTo);
        Assert.Equal(4, changes);
        Assert.Equal(3, await source.CountAsync(Token));
        Assert.Null(source.Summary);
    }

    private async Task OpenLibraryAsync(int count)
    {
        library = new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "search"), "search");
        var folder = IndexedFolder.Create(library.Library.Id, @"D:\Photos", @"D:\Photos", Now);
        library.FolderList.Folders.Add(folder);
        for (var number = 0; number < count; number++)
        {
            library.ItemList.Items.Add(MediaItem.Discover(folder.Id, RelativePath.From($"album/IMG_{number}.jpg"), MediaType.Image, 100, Now, Now, null, Now));
        }

        // The fake lists items newest first by when they were added; keep that the UUIDv7 order, as in a library.
        library.ItemList.Items.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));

        await session.SetCurrentAsync(library);
    }

    /// <summary>
    /// Answers <see cref="SearchMediaQuery"/> with the hits the test sets, and passes the other requests on.
    /// </summary>
    private sealed class StubSender(ISender inner) : ForwardingSender(inner)
    {
        public IReadOnlyList<SearchHit> Hits { get; set; } = [];

        public int LessRelevantCount { get; set; }

        public SemanticSearchStatus Semantic { get; set; } = SemanticSearchStatus.Used;

        public TaskCompletionSource? Block { get; set; }

        public bool IgnoreCancellation { get; set; }

        public TaskCompletionSource Started { get; } = new();

        public int SearchCalls { get; private set; }

        public SearchMediaQuery? LastQuery { get; private set; }

        public FindSimilarQuery? LastSimilar { get; private set; }

        public override async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is FindSimilarQuery similar)
            {
                SearchCalls++;
                LastSimilar = similar;
                var hits = Semantic == SemanticSearchStatus.NotAnalyzed ? [] : Hits;
                return (TResponse)(object)new MediaSearchResults([.. hits.Skip(similar.Offset).Take(similar.Limit)], hits.Count, [], SearchMode.Similar, Semantic);
            }

            if (request is not SearchMediaQuery query)
            {
                return await base.Send(request, cancellationToken);
            }

            SearchCalls++;
            LastQuery = query;
            if (Block is { } block)
            {
                Started.TrySetResult();
                await block.Task.WaitAsync(IgnoreCancellation ? CancellationToken.None : cancellationToken);
            }

            var page = Hits.Skip(query.Offset).Take(query.Limit).ToList();
            var mode = Semantic == SemanticSearchStatus.Used ? SearchMode.Hybrid : SearchMode.Keyword;
            return (TResponse)(object)new MediaSearchResults(page, Hits.Count, [], mode, Semantic, LessRelevantCount);
        }
    }
}
