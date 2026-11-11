using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.UseCases.Search.FindSimilar;
using MediaFileClassifier.UseCases.Search.SearchMedia;

namespace MediaFileClassifier.Infrastructure.Tests.Search;

/// <summary>
/// Tests for <see cref="FindSimilarQueryHandler"/> (E09-S03-T01) against a real library and search index.
/// </summary>
public sealed class FindSimilarQueryHandlerTests : IAsyncLifetime
{
    private SearchTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await SearchTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task FindSimilar_NearDuplicatesRankAtTheTop_AndTheSourceIsNotAResult()
    {
        var sample = await bed.AddItemAsync(1, vector: [1, 0.1f, 0, 0]);
        var unrelated = await bed.AddItemAsync(2, vector: [0, 0, 1, 0]);
        var resized = await bed.AddItemAsync(3, vector: [1, 0.12f, 0.01f, 0]);
        var related = await bed.AddItemAsync(4, vector: [0.7f, 0.7f, 0, 0]);
        var cropped = await bed.AddItemAsync(5, vector: [1, 0.15f, 0.03f, 0]);

        var results = await SendAsync(new FindSimilarQuery(sample.Id));

        Assert.Equal(SearchMode.Similar, results.Mode);
        Assert.Equal(SemanticSearchStatus.Used, results.Semantic);
        Assert.Equal([resized.Id, cropped.Id, related.Id, unrelated.Id], results.Hits.Select(hit => hit.ItemId));
        Assert.Equal(4, results.TotalCount);
        Assert.All(results.Hits.Take(2), hit => Assert.True(hit.Similarity > 0.99f));
        Assert.All(results.Hits, hit => Assert.False(hit.IsLessRelevant));
        Assert.Empty(results.Terms);
    }

    [Fact]
    public async Task FindSimilar_Video_UsesItsItemLevelVector()
    {
        var video = await bed.AddItemAsync(1);
        var frames = new[]
        {
            new Keyframe(Id<Keyframe>.New(), video.Id, 0, TimeSpan.Zero, 0),
            new Keyframe(Id<Keyframe>.New(), video.Id, 1, TimeSpan.FromSeconds(5), 150),
        };
        await bed.Library.Library.Keyframes.ReplaceAsync(video.Id, frames, Token);

        // The keyframe vectors point elsewhere; only the item-level vector (the mean, D228) is like "match".
        var model = bed.Library.Model;
        await bed.Library.Library.Embeddings.ReplaceAsync(
            video.Id,
            model.Id,
            [
                Embedding.Create(video.Id, model, EmbeddingKind.Image, [0, 0, 1, 0], bed.Library.Clock.Tick(), frames[0].Id),
                Embedding.Create(video.Id, model, EmbeddingKind.Image, [0, 0, 0, 1], bed.Library.Clock.Tick(), frames[1].Id),
                Embedding.Create(video.Id, model, EmbeddingKind.Image, [1, 0, 0, 0], bed.Library.Clock.Tick()),
            ],
            Token);
        var keyframeLike = await bed.AddItemAsync(2, vector: [0, 0, 1, 0.1f]);
        var match = await bed.AddItemAsync(3, vector: [1, 0.05f, 0, 0]);

        var results = await SendAsync(new FindSimilarQuery(video.Id));

        Assert.Equal(match.Id, results.Hits[0].ItemId);
        Assert.Equal(keyframeLike.Id, results.Hits[1].ItemId);
    }

    [Fact]
    public async Task FindSimilar_KeepsToTheFilter()
    {
        var sample = await bed.AddItemAsync(1, vector: [1, 0, 0, 0]);
        await bed.AddItemAsync(2, vector: [1, 0.1f, 0, 0]);

        var results = await SendAsync(new FindSimilarQuery(sample.Id) { Filter = new SearchFilter { FolderId = Id<IndexedFolder>.New() } });

        Assert.Equal(SemanticSearchStatus.Used, results.Semantic);
        Assert.Empty(results.Hits);
        Assert.Equal(0, results.TotalCount);
    }

    [Fact]
    public async Task FindSimilar_ItemWithoutAnEmbedding_IsNotAnalyzed()
    {
        var unanalyzed = await bed.AddItemAsync(1);
        await bed.AddItemAsync(2, vector: [1, 0, 0, 0]);

        var results = await SendAsync(new FindSimilarQuery(unanalyzed.Id));

        Assert.Equal(SearchMode.Similar, results.Mode);
        Assert.Equal(SemanticSearchStatus.NotAnalyzed, results.Semantic);
        Assert.Empty(results.Hits);
    }

    [Fact]
    public async Task FindSimilar_NoLibrary_IsUnavailable()
    {
        var sample = await bed.AddItemAsync(1, vector: [1, 0, 0, 0]);
        var handler = new FindSimilarQueryHandler(bed.Index, new TestSession(null));

        var results = await handler.Handle(new FindSimilarQuery(sample.Id), Token);

        Assert.Equal(SemanticSearchStatus.Unavailable, results.Semantic);
        Assert.Empty(results.Hits);
    }

    private Task<MediaSearchResults> SendAsync(FindSimilarQuery query) =>
        new FindSimilarQueryHandler(bed.Index, bed.Session).Handle(query, Token);
}
