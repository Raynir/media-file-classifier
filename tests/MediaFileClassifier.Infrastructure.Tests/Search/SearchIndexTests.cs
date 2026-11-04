using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Search;

/// <summary>
/// End-to-end tests for <see cref="SearchIndex"/> (E01-S04-T04): keyword, vector and hybrid modes, ordering, paging,
/// and following the library session.
/// </summary>
public sealed class SearchIndexTests : IAsyncLifetime
{
    private SearchTestBed bed = null!;

    public async ValueTask InitializeAsync() => bed = await SearchTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task Hybrid_MergesBothListsWithRrf()
    {
        var keywordOnly = await bed.AddItemAsync(1, title: "beach");
        var both = await bed.AddItemAsync(2, title: "beach", vector: [0.8f, 0.6f, 0, 0]);
        var vectorOnly = await bed.AddItemAsync(3, vector: [1, 0, 0, 0]);
        await bed.AddItemAsync(4, title: "mountain");

        var results = await bed.SearchAsync(new SearchQuery { Text = "beach", Vector = bed.Vector(1, 0, 0, 0) });

        // keywordOnly: 1/61; both: 1/62 + 1/62; vectorOnly: 1/61, tied with keywordOnly, which wins on the keyword list.
        // The mountain item matches neither list.
        Assert.Equal([both.Id, keywordOnly.Id, vectorOnly.Id], results.Hits.Select(hit => hit.ItemId));
        Assert.Equal(3, results.TotalCount);
        Assert.Equal(2, results.Hits[0].KeywordRank);
        Assert.Equal(0.8f, results.Hits[0].Similarity!.Value, 3);
        Assert.Null(results.Hits[1].Similarity);
        Assert.Null(results.Hits[2].KeywordRank);
        Assert.Equal(SearchFields.Title, results.Hits[0].MatchedFields);
        Assert.Equal(SearchFields.None, results.Hits[2].MatchedFields);
        Assert.Equal(["beach"], results.Terms);
        Assert.False(results.VectorSearchSkipped);
    }

    [Fact]
    public async Task Hybrid_WeightsAreConfigurable()
    {
        var keywordOnly = await bed.AddItemAsync(1, title: "beach");
        var vectorOnly = await bed.AddItemAsync(2, vector: [1, 0, 0, 0]);

        var results = await bed.SearchAsync(new SearchQuery
        {
            Text = "beach",
            Vector = bed.Vector(1, 0, 0, 0),
            Ranking = SearchRankingOptions.Default with { VectorWeight = 2 },
        });

        Assert.Equal([vectorOnly.Id, keywordOnly.Id], results.Hits.Select(hit => hit.ItemId));
    }

    [Fact]
    public async Task VectorOnly_RanksBySimilarity()
    {
        var far = await bed.AddItemAsync(1, vector: [0, 1, 0, 0]);
        var near = await bed.AddItemAsync(2, vector: [1, 0.1f, 0, 0]);
        var middle = await bed.AddItemAsync(3, vector: [1, 1, 0, 0]);
        await bed.AddItemAsync(4);

        var results = await bed.SearchAsync(new SearchQuery { Vector = bed.Vector(1, 0, 0, 0) });

        Assert.Equal([near.Id, middle.Id, far.Id], results.Hits.Select(hit => hit.ItemId));
        Assert.Equal(results.Hits.Select(hit => (double)hit.Similarity!.Value), results.Hits.Select(hit => hit.Score));
        Assert.Empty(results.Terms);
    }

    [Fact]
    public async Task VectorWrittenAfterOpening_IsFoundByTheNextQuery()
    {
        var item = await bed.AddItemAsync(1);
        Assert.Empty((await bed.SearchAsync(new SearchQuery { Vector = bed.Vector(0, 0, 1, 0) })).Hits);

        await bed.SetVectorAsync(item, [0, 0, 1, 0]);

        Assert.Equal([item.Id], (await bed.SearchAsync(new SearchQuery { Vector = bed.Vector(0, 0, 1, 0) })).Hits.Select(hit => hit.ItemId));
    }

    [Fact]
    public async Task ItemWithSeveralVectors_ScoresAsItsBestVector()
    {
        var item = await bed.AddItemAsync(1);
        await bed.SetVectorAsync(item, [0, 1, 0, 0], [1, 0, 0, 0]);
        var other = await bed.AddItemAsync(2, vector: [1, 0.5f, 0, 0]);

        var results = await bed.SearchAsync(new SearchQuery { Vector = bed.Vector(1, 0, 0, 0) });

        Assert.Equal([item.Id, other.Id], results.Hits.Select(hit => hit.ItemId));
        Assert.Equal(1f, results.Hits[0].Similarity!.Value, 3);
    }

    [Fact]
    public async Task FindSimilar_LeavesTheItemOut()
    {
        var start = await bed.AddItemAsync(1, vector: [1, 0, 0, 0]);
        var near = await bed.AddItemAsync(2, vector: [1, 0.2f, 0, 0]);
        var far = await bed.AddItemAsync(3, vector: [0, 1, 0, 0]);

        var results = await bed.SearchAsync(new SearchQuery { Vector = SearchVector.FromItem(bed.Library.Model.Id, start.Id) });

        Assert.Equal([near.Id, far.Id], results.Hits.Select(hit => hit.ItemId));
    }

    [Fact]
    public async Task NeitherTextNorVector_ListsTheFilteredItemsNewestFirst()
    {
        var first = await bed.AddItemAsync(1);
        var second = await bed.AddItemAsync(2);

        var results = await bed.SearchAsync(new SearchQuery());

        Assert.Equal([second.Id, first.Id], results.Hits.Select(hit => hit.ItemId));
        Assert.Equal(2, results.TotalCount);
    }

    [Fact]
    public async Task KeywordOnly_PagesThroughEveryMatch()
    {
        for (var i = 1; i <= 5; i++)
        {
            await bed.AddItemAsync(i, title: "harbour");
        }

        var page = await bed.SearchAsync(new SearchQuery { Text = "harbour", Offset = 2, Limit = 2 });

        Assert.Equal(5, page.TotalCount);
        Assert.Equal([3, 4], page.Hits.Select(hit => hit.KeywordRank!.Value));
        Assert.All(page.Hits, hit => Assert.True(hit.Score > 0));
    }

    [Fact]
    public async Task Hybrid_SortedByName_SortsTheRankedSet()
    {
        var b = await bed.AddItemAsync(2, title: "beach");
        var a = await bed.AddItemAsync(1, vector: [1, 0, 0, 0]);
        await bed.AddItemAsync(3, title: "mountain");

        var results = await bed.SearchAsync(new SearchQuery { Text = "beach", Vector = bed.Vector(1, 0, 0, 0), Sort = SortMode.Name });

        Assert.Equal([a.Id, b.Id], results.Hits.Select(hit => hit.ItemId));
        Assert.Equal(2, results.TotalCount);
    }

    [Fact]
    public async Task ReverseSort_ReversesRelevance()
    {
        var near = await bed.AddItemAsync(1, vector: [1, 0, 0, 0]);
        var far = await bed.AddItemAsync(2, vector: [0, 1, 0, 0]);

        var results = await bed.SearchAsync(new SearchQuery { Vector = bed.Vector(1, 0, 0, 0), ReverseSort = true });

        Assert.Equal([far.Id, near.Id], results.Hits.Select(hit => hit.ItemId));
    }

    [Fact]
    public async Task CandidateLimit_CapsVectorResults()
    {
        for (var i = 1; i <= 6; i++)
        {
            await bed.AddItemAsync(i, vector: [1, i, 0, 0]);
        }

        var results = await bed.SearchAsync(new SearchQuery { Vector = bed.Vector(1, 0, 0, 0), Ranking = SearchRankingOptions.Default with { CandidateLimit = 4 } });

        Assert.Equal(4, results.TotalCount);
        Assert.Equal(4, results.Hits.Count);
    }

    [Fact]
    public async Task QueryVectorOfAnotherDimension_Throws()
    {
        await bed.AddItemAsync(1, vector: [1, 0, 0, 0]);

        await Assert.ThrowsAsync<ArgumentException>(() => bed.SearchAsync(new SearchQuery { Vector = bed.Vector(1, 0, 0) }));
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    [InlineData(0, SearchQuery.MaxLimit + 1)]
    public async Task InvalidPage_Throws(int offset, int limit)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => bed.SearchAsync(new SearchQuery { Offset = offset, Limit = limit }));
    }

    [Fact]
    public async Task NoLibrary_FindsNothing_AndVectorsAreUnavailable()
    {
        await bed.Session.SetCurrentAsync(null);

        Assert.Equal(VectorIndexState.Unavailable, bed.Index.VectorState);
        Assert.Same(SearchResults.Empty, await bed.SearchAsync(new SearchQuery { Text = "x" }));
    }

    [Fact]
    public async Task ReopenedLibrary_LoadsItsVectorsFromTheDatabase()
    {
        var item = await bed.AddItemAsync(1, vector: [0, 0, 0, 1]);
        var states = new List<VectorIndexState>();
        bed.Index.VectorStateChanged += (_, _) => states.Add(bed.Index.VectorState);
        await bed.Session.SetCurrentAsync(null);
        await bed.Library.ReopenAsync();

        await bed.Session.SetCurrentAsync(bed.Library.Library);
        await bed.Index.Ready;

        Assert.Equal(VectorIndexState.Ready, bed.Index.VectorState);
        Assert.Contains(VectorIndexState.Unavailable, states);
        Assert.Equal([item.Id], (await bed.SearchAsync(new SearchQuery { Vector = bed.Vector(0, 0, 0, 1) })).Hits.Select(hit => hit.ItemId));
    }

    [Fact]
    public async Task AddSearch_RegistersOneSearchIndex()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<Abstractions.Libraries.ILibrarySession>(bed.Session)
            .AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>))
            .AddSearch()
            .BuildServiceProvider();

        var index = provider.GetRequiredService<SearchIndex>();
        await index.Ready;

        Assert.Same(index, provider.GetRequiredService<ISearchIndex>());
    }

    [Fact]
    public async Task DeletedItem_IsNotFoundByVector()
    {
        var item = await bed.AddItemAsync(1, vector: [1, 0, 0, 0]);

        await bed.Library.Library.Items.DeleteAsync(item.Id, TestContext.Current.CancellationToken);

        Assert.Empty((await bed.SearchAsync(new SearchQuery { Vector = bed.Vector(1, 0, 0, 0) })).Hits);
    }
}
