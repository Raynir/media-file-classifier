using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.AI;
using MediaFileClassifier.Testing.AI;
using MediaFileClassifier.UseCases.Embeddings;
using MediaFileClassifier.UseCases.Search.SearchMedia;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Search;

/// <summary>
/// Tests for <see cref="SearchMediaQueryHandler"/> (E09-S01) against a real library and search index, with a fake
/// text embedder whose vectors the tests place items around.
/// </summary>
public sealed class SearchMediaQueryHandlerTests : IAsyncLifetime
{
    private const int Dimension = 8;

    private SearchTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await SearchTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task Search_SampleQueries_ReturnFusedResults()
    {
        var provider = new FakeEmbeddingProvider(dimension: Dimension);
        var model = await RegisterAsync(provider);
        var titled = await bed.AddItemAsync(1, title: "red car");
        var both = await bed.AddItemAsync(2, title: "red car on a road");
        var pictured = await bed.AddItemAsync(3);
        await bed.AddItemAsync(4, title: "blue boat");
        await SetVectorAsync(model, both, Around("red car", 0.6f));
        await SetVectorAsync(model, pictured, Around("red car", 0.9f));
        using var handler = Create(provider);

        var results = await handler.SearchAsync(new SearchMediaQuery { Text = "red car" });

        Assert.Equal(SearchMode.Hybrid, results.Mode);
        Assert.Equal(SemanticSearchStatus.Used, results.Semantic);
        Assert.Equal(["red", "car"], results.Terms);
        Assert.Equal(3, results.TotalCount);

        // "both" is in both lists, so fusion puts it first; the other two come from one list each.
        Assert.Equal(both.Id, results.Hits[0].ItemId);
        Assert.Equivalent(new[] { both.Id, titled.Id, pictured.Id }, results.Hits.Select(hit => hit.ItemId));
        Assert.Equal(0.6f, results.Hits[0].Similarity!.Value, 2);
        Assert.NotNull(results.Hits[0].KeywordRank);
        Assert.Equal(SearchFields.Title, results.Hits[0].MatchedFields);
        Assert.Contains(results.Hits, hit => hit.ItemId == pictured.Id && hit.KeywordRank is null && hit.Similarity > 0.89f);
        Assert.Contains(results.Hits, hit => hit.ItemId == titled.Id && hit.Similarity is null);
    }

    [Fact]
    public async Task Search_EmptyText_BrowsesNewestTakenFirst()
    {
        var provider = new FakeEmbeddingProvider(dimension: Dimension);
        // Item 1 was modified a day after item 2 but imported first, so newest import first would be the other way round.
        var recent = await bed.AddItemAsync(1);
        var old = await bed.AddItemAsync(2);
        using var handler = Create(provider);

        var results = await handler.SearchAsync(new SearchMediaQuery { Text = "   " });

        Assert.Equal(SearchMode.Browse, results.Mode);
        Assert.Equal(SemanticSearchStatus.NotNeeded, results.Semantic);
        Assert.Equal([recent.Id, old.Id], results.Hits.Select(hit => hit.ItemId));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Search_ModelMissing_FallsBackToKeywords()
    {
        var provider = new FakeEmbeddingProvider(new FakeProviderBehavior { FailureRate = 1, FailureKind = AiFailureKind.ModelMissing }, Dimension);
        var titled = await bed.AddItemAsync(1, title: "red car");
        using var handler = Create(provider);

        var results = await handler.SearchAsync(new SearchMediaQuery { Text = "red car" });

        Assert.Equal(SearchMode.Keyword, results.Mode);
        Assert.Equal(SemanticSearchStatus.ModelMissing, results.Semantic);
        Assert.Equal([titled.Id], results.Hits.Select(hit => hit.ItemId));
    }

    [Fact]
    public async Task Search_EncoderFails_FallsBackToKeywords()
    {
        var provider = new FakeEmbeddingProvider(new FakeProviderBehavior { FailureRate = 1 }, Dimension);
        var titled = await bed.AddItemAsync(1, title: "red car");
        using var handler = Create(provider);

        var results = await handler.SearchAsync(new SearchMediaQuery { Text = "red car" });

        Assert.Equal(SemanticSearchStatus.Failed, results.Semantic);
        Assert.Equal([titled.Id], results.Hits.Select(hit => hit.ItemId));
    }

    [Fact]
    public async Task Search_NoProvider_IsKeywordOnly()
    {
        var titled = await bed.AddItemAsync(1, title: "red car");
        using var handler = Create(null);

        var results = await handler.SearchAsync(new SearchMediaQuery { Text = "red car" });

        Assert.Equal(SemanticSearchStatus.Unavailable, results.Semantic);
        Assert.Equal(SearchMode.Keyword, results.Mode);
        Assert.Equal([titled.Id], results.Hits.Select(hit => hit.ItemId));
    }

    [Fact]
    public async Task Search_OnlySymbols_IsSemanticOnly()
    {
        var provider = new FakeEmbeddingProvider(dimension: Dimension);
        var model = await RegisterAsync(provider);
        var pictured = await bed.AddItemAsync(1);
        await SetVectorAsync(model, pictured, Around("!!!", 0.95f));
        using var handler = Create(provider);

        var results = await handler.SearchAsync(new SearchMediaQuery { Text = "!!!" });

        Assert.Equal(SearchMode.Semantic, results.Mode);
        Assert.Equal([pictured.Id], results.Hits.Select(hit => hit.ItemId));
    }

    [Fact]
    public async Task Search_Cancelled_Throws()
    {
        var provider = new FakeEmbeddingProvider(dimension: Dimension);
        using var handler = Create(provider);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.Handler.Handle(new SearchMediaQuery { Text = "red car" }, cancelled.Token));
    }

    [Theory]
    [InlineData("\"")]
    [InlineData("\"red")]
    [InlineData("red\" car")]
    [InlineData("'")]
    [InlineData("*")]
    [InlineData("red*")]
    [InlineData("^red")]
    [InlineData("-red +car")]
    [InlineData("red AND car OR NOT boat")]
    [InlineData("NEAR(red car, 2)")]
    [InlineData("title:red")]
    [InlineData("{title description}: red")]
    [InlineData("(red")]
    [InlineData("red)")]
    [InlineData(@"[x] % _ \ ; --")]
    [InlineData("a\0b")]
    [InlineData("😀 🐱")]
    [InlineData("猫の写真")]
    [InlineData("صورة قطة")]
    [InlineData("כלב")]
    [InlineData("ศาลา")]
    [InlineData("e\u0301\u0301\u0301")]
    [InlineData("\u200B\u200D\uFEFF")]
    [InlineData("cat\uD800")]
    public async Task Search_AnySymbolsOrScript_NeverFails(string text)
    {
        var provider = new FakeEmbeddingProvider(dimension: Dimension);
        await bed.AddItemAsync(1, title: "red car \"quoted\" кот 猫");
        using var keywordOnly = Create(null);
        using var hybrid = Create(provider);

        var keyword = await keywordOnly.SearchAsync(new SearchMediaQuery { Text = text });
        var both = await hybrid.SearchAsync(new SearchMediaQuery { Text = text, Sort = SortMode.Name });

        Assert.NotNull(keyword);
        Assert.NotNull(both);
    }

    [Fact]
    public async Task Search_LongPastedText_NeverFails()
    {
        var provider = new FakeEmbeddingProvider(dimension: Dimension);
        await bed.AddItemAsync(1, title: "red car");
        using var handler = Create(provider);
        var text = string.Join(' ', Enumerable.Range(0, 400).Select(i => $"word{i}\"*"));

        var results = await handler.SearchAsync(new SearchMediaQuery { Text = text });

        Assert.DoesNotContain(results.Hits, hit => hit.KeywordRank is not null);
    }

    [Theory]
    [InlineData("кот", 1)]
    [InlineData("КОТ", 1)]
    [InlineData("ёлка", 2)]
    [InlineData("елка", 2)]
    [InlineData("e\u0301te\u0301", 3)]
    [InlineData("\"quoted\"", 4)]
    [InlineData("  red\u00A0car  ", 5)]
    [InlineData("猫", 6)]
    public async Task Search_NonLatinAndOddlyTypedText_FindsTheItem(string text, int expected)
    {
        var items = new[]
        {
            await bed.AddItemAsync(1, title: "кот на окне"),
            await bed.AddItemAsync(2, title: "Ёлка в снегу"),
            await bed.AddItemAsync(3, title: "Été à Paris"),
            await bed.AddItemAsync(4, title: "the \"quoted\" sign"),
            await bed.AddItemAsync(5, title: "red car"),
            await bed.AddItemAsync(6, title: "猫"),
        };
        using var handler = Create(null);

        var results = await handler.SearchAsync(new SearchMediaQuery { Text = text });

        Assert.Equal([items[expected - 1].Id], results.Hits.Select(hit => hit.ItemId));
    }

    [Fact]
    public async Task Search_ModelWithAThreshold_HidesNonsenseButKeepsRealMatches()
    {
        // Many dimensions keep chance similarities near 0 (about 0.016 apart), far below the 0.07 bar.
        const int Wide = 4096;
        var provider = new FakeEmbeddingProvider(dimension: Wide, modelName: "siglip2-base-patch16-256");
        var model = await RegisterAsync(provider);
        var cars = new List<MediaItem>();
        for (var i = 0; i < 3; i++)
        {
            var car = await bed.AddItemAsync(i + 1);
            await SetVectorAsync(model, car, Around("red car", 0.3f - (i * 0.05f), Wide));
            cars.Add(car);
            var dog = await bed.AddItemAsync(i + 11);
            await SetVectorAsync(model, dog, Around("a dog", 0.3f - (i * 0.05f), Wide));
        }

        for (var i = 0; i < 6; i++)
        {
            var other = await bed.AddItemAsync(i + 21);
            await SetVectorAsync(model, other, FakeEmbeddingProvider.VectorFor($"filler {i}", Wide));
        }

        using var handler = Create(provider);

        var real = await handler.SearchAsync(new SearchMediaQuery { Text = "red car" });
        var nonsense = await handler.SearchAsync(new SearchMediaQuery { Text = "xqzv plorft" });
        var nonsenseShown = await handler.SearchAsync(new SearchMediaQuery { Text = "xqzv plorft", ShowLessRelevant = true });

        Assert.Equal(cars.Select(item => item.Id), real.Hits.Select(hit => hit.ItemId));
        Assert.Equal(9, real.LessRelevantCount);
        Assert.Empty(nonsense.Hits);
        Assert.Equal(0, nonsense.TotalCount);
        Assert.Equal(12, nonsense.LessRelevantCount);
        Assert.Equal(12, nonsenseShown.Hits.Count);
        Assert.All(nonsenseShown.Hits, hit => Assert.True(hit.IsLessRelevant));
    }

    [Fact]
    public async Task Search_ModelWithoutAThreshold_ShowsEveryNeighbour()
    {
        var provider = new FakeEmbeddingProvider(dimension: Dimension, modelName: "unmeasured-model");
        var model = await RegisterAsync(provider);
        var item = await bed.AddItemAsync(1);
        await SetVectorAsync(model, item, FakeEmbeddingProvider.VectorFor("filler", Dimension));
        using var handler = Create(provider);

        var results = await handler.SearchAsync(new SearchMediaQuery { Text = "xqzv plorft" });

        Assert.Equal([item.Id], results.Hits.Select(hit => hit.ItemId));
        Assert.Equal(0, results.LessRelevantCount);
    }

    /// <summary>
    /// Makes a unit vector whose cosine to the text's query vector is <paramref name="similarity"/>.
    /// </summary>
    /// <param name="text">The query text.</param>
    /// <param name="similarity">The cosine wanted, from 0 to 1.</param>
    /// <param name="dimension">The vector length.</param>
    /// <returns>The vector.</returns>
    internal static float[] Around(string text, float similarity, int dimension = Dimension)
    {
        var query = FakeEmbeddingProvider.VectorFor(text.ToLowerInvariant(), dimension);
        var other = FakeEmbeddingProvider.VectorFor("orthogonal " + text, dimension);
        var dot = query.Zip(other, (a, b) => a * b).Sum();
        var orthogonal = other.Select((value, i) => value - (dot * query[i])).ToArray();
        var norm = MathF.Sqrt(orthogonal.Sum(value => value * value));
        var rest = MathF.Sqrt(1 - (similarity * similarity));
        return [.. query.Select((value, i) => (similarity * value) + (rest * orthogonal[i] / norm))];
    }

    private async Task<ModelRegistration> RegisterAsync(IAiProvider provider) =>
        await bed.Library.Library.Models.GetOrAddAsync(provider.Model, bed.Library.Clock.GetUtcNow(), Token);

    private Task SetVectorAsync(ModelRegistration model, MediaItem item, float[] vector) =>
        bed.Library.Library.Embeddings.ReplaceAsync(
            item.Id,
            model.Id,
            [Embedding.Create(item.Id, model, EmbeddingKind.Image, vector, bed.Library.Clock.Tick())],
            Token);

    private TestHandler Create(FakeEmbeddingProvider? provider)
    {
        var registry = new AiProviderRegistry(provider is null ? [] : [provider], bed.Library.Clock);
        var encoder = new SemanticQueryEncoder(registry, bed.Session, bed.Library.Clock);
        return new TestHandler(encoder, new SearchMediaQueryHandler(bed.Index, encoder, registry, NullLogger<SearchMediaQueryHandler>.Instance));
    }

    private sealed class TestHandler(SemanticQueryEncoder encoder, SearchMediaQueryHandler handler) : IDisposable
    {
        public SearchMediaQueryHandler Handler => handler;

        public Task<MediaSearchResults> SearchAsync(SearchMediaQuery query) => handler.Handle(query, Token);

        public void Dispose() => encoder.Dispose();
    }
}
