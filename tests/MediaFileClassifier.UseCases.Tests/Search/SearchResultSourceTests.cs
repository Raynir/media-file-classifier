using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.UseCases.Browsing;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
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
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

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
    public async Task Ids_FollowTheSearchOrder_FromAnItemOn_AcrossPages()
    {
        await OpenLibraryAsync(5);
        var items = library!.ItemList.Items;
        sender.Hits = [.. new[] { 3, 1, 4, 0 }.Select(index => new SearchHit(items[index].Id, 1, 1, null, SearchFields.Title))];
        source.SetText("sign");

        var all = await source.GetIdsAsync(0, 10, null, Token);
        var fromItem = await source.GetIdsAsync(1, 10, items[1].Id, Token);
        var fromItemOn = await source.GetIdsAsync(0, 10, items[1].Id, Token);

        Assert.Equal([items[3].Id, items[1].Id, items[4].Id, items[0].Id], all);
        Assert.Equal([items[4].Id, items[0].Id], fromItem);
        Assert.Equal([items[1].Id, items[4].Id, items[0].Id], fromItemOn);
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

        await session.SetCurrentAsync(library);
    }

    /// <summary>
    /// Answers <see cref="SearchMediaQuery"/> with the hits the test sets, and passes the other requests on.
    /// </summary>
    private sealed class StubSender(ISender inner) : ISender
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

        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
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
                return await inner.Send(request, cancellationToken);
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

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => inner.Send(request, cancellationToken);

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => inner.Send(request, cancellationToken);

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            inner.CreateStream(request, cancellationToken);

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            inner.CreateStream(request, cancellationToken);
    }
}
