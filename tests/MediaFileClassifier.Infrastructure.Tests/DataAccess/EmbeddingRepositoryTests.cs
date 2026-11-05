using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.DataAccess.Repositories;
using MediaFileClassifier.Infrastructure.Search.Vectors;
using MediaFileClassifier.Infrastructure.Tests.Search;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Tests for <see cref="EmbeddingRepository"/> (E01-S04-T03): the single write path for embeddings, its post-commit
/// event, and the bulk read the vector index loads from.
/// </summary>
public sealed class EmbeddingRepositoryTests : IAsyncLifetime
{
    private LibraryTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await LibraryTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task ReplaceAsync_StoresTheVectors_AndRaisesChangedAfterCommit()
    {
        var item = await AddItemAsync(1);
        var embedding = Embedding.Create(item.Id, bed.Model, EmbeddingKind.Image, [0.5f, 0.5f, 0.5f, 0.5f], bed.Clock.Tick());
        EmbeddingsChangedEventArgs? raised = null;
        var storedWhenRaised = -1;
        bed.Library.Embeddings.Changed += (_, args) =>
        {
            raised = args;

            // Raised after the commit: another connection already sees the row.
            storedWhenRaised = bed.Library.Embeddings.GetAsync(item.Id, Token).GetAwaiter().GetResult().Count;
        };

        await bed.Library.Embeddings.ReplaceAsync(item.Id, bed.Model.Id, [embedding], Token);

        var change = Assert.Single(raised!.Changes);
        Assert.Equal(item.Id, change.Embeddings.ItemId);
        Assert.Equal(bed.Model.Id, change.Embeddings.ModelId);
        Assert.True(change.DocumentId > 0);
        Assert.Equal(1, storedWhenRaised);
        var stored = Assert.Single(await bed.Library.Embeddings.GetAsync(item.Id, Token));
        Assert.Equal(embedding.Vector.ToArray(), stored.Vector.ToArray());
    }

    [Fact]
    public async Task ReplaceAsync_ReplacesOnlyThatModelsVectors()
    {
        var item = await AddItemAsync(1);
        var other = ModelRegistration.Create("local", "other", "1", null, 4, LibraryTestBed.Start);
        await bed.Library.Models.AddAsync(other, Token);
        await bed.Library.Embeddings.ReplaceAsync(item.Id, other.Id, [Embedding.Create(item.Id, other, EmbeddingKind.Image, [1, 0, 0, 0], bed.Clock.Tick())], Token);
        await bed.Library.Embeddings.ReplaceAsync(item.Id, bed.Model.Id, [Embedding.Create(item.Id, bed.Model, EmbeddingKind.Image, [0, 1, 0, 0], bed.Clock.Tick())], Token);

        await bed.Library.Embeddings.ReplaceAsync(
            item.Id,
            bed.Model.Id,
            [Embedding.Create(item.Id, bed.Model, EmbeddingKind.Image, [0, 0, 1, 0], bed.Clock.Tick()), Embedding.Create(item.Id, bed.Model, EmbeddingKind.DerivedText, [0, 0, 0, 1], bed.Clock.Tick())],
            Token);

        var stored = await bed.Library.Embeddings.GetAsync(item.Id, Token);
        Assert.Single(stored, embedding => embedding.ModelId == other.Id);
        Assert.Equal(2, stored.Count(embedding => embedding.ModelId == bed.Model.Id));
    }

    [Fact]
    public async Task ReplaceAsync_EmbeddingOfAnotherItem_Throws()
    {
        var item = await AddItemAsync(1);
        var embedding = Embedding.Create(Id<MediaItem>.New(), bed.Model, EmbeddingKind.Image, [1, 0, 0, 0], bed.Clock.Tick());

        await Assert.ThrowsAsync<ArgumentException>(() => bed.Library.Embeddings.ReplaceAsync(item.Id, bed.Model.Id, [embedding], Token));
    }

    [Fact]
    public async Task ReplaceAsync_UnknownItem_ThrowsKeyNotFound()
    {
        var itemId = Id<MediaItem>.New();
        var embedding = Embedding.Create(itemId, bed.Model, EmbeddingKind.Image, [1, 0, 0, 0], bed.Clock.Tick());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => bed.Library.Embeddings.ReplaceAsync(itemId, bed.Model.Id, [embedding], Token));
    }

    [Fact]
    public async Task ReplaceRangeAsync_SkipsDeletedItems_AndRaisesOncePerBatch()
    {
        var items = new List<MediaItem>();
        for (var i = 1; i <= 150; i++)
        {
            items.Add(await AddItemAsync(i));
        }

        var gone = Id<MediaItem>.New();
        var batches = new List<int>();
        bed.Library.Embeddings.Changed += (_, args) => batches.Add(args.Changes.Count);
        var changes = items.Select(item => item.Id).Append(gone)
            .Select(id => new ItemEmbeddings(id, bed.Model.Id, [Embedding.Create(id, bed.Model, EmbeddingKind.Image, [1, 0, 0, 0], bed.Clock.Tick())]))
            .ToList();

        await bed.Library.Embeddings.ReplaceRangeAsync(changes, Token);

        Assert.Equal([100, 50], batches);
    }

    [Fact]
    public async Task DeletedItem_LosesItsEmbeddings()
    {
        var item = await AddItemAsync(1);
        await bed.Library.Embeddings.ReplaceAsync(item.Id, bed.Model.Id, [Embedding.Create(item.Id, bed.Model, EmbeddingKind.Image, [1, 0, 0, 0], bed.Clock.Tick())], Token);

        await bed.Library.Items.DeleteAsync(item.Id, Token);

        Assert.Empty(await bed.Library.Embeddings.GetAsync(item.Id, Token));
        var read = 0;
        await bed.Library.Embeddings.ReadAllVectorsAsync((in StoredVector _, ReadOnlySpan<byte> _) => read++, Token);
        Assert.Equal(0, read);
    }

    [Fact]
    public async Task IndexLoadedFromTheDatabase_AnswersLikeTheIndexBuiltFromTheEvents()
    {
        var random = new Random(5);
        using var fromEvents = new VectorIndex();
        bed.Library.Embeddings.Changed += (_, args) => fromEvents.Apply(args.Changes);
        var changes = new List<ItemEmbeddings>();
        for (var i = 1; i <= 120; i++)
        {
            var item = await AddItemAsync(i);
            changes.Add(new ItemEmbeddings(
                item.Id,
                bed.Model.Id,
                [.. Enumerable.Range(0, random.Next(1, 4)).Select(_ => Embedding.Create(item.Id, bed.Model, EmbeddingKind.Image, SyntheticItems.RandomUnit(random, 4), bed.Clock.Tick()))]));
        }

        await bed.Library.Embeddings.ReplaceRangeAsync(changes, Token);

        using var loaded = await VectorIndex.LoadAsync(bed.Library.Embeddings, Token);
        Assert.Equal(fromEvents.VectorCount, loaded.VectorCount);
        for (var q = 0; q < 10; q++)
        {
            var query = SearchVector.FromVector(bed.Model.Id, SyntheticItems.RandomUnit(random, 4));
            Assert.Equal(fromEvents.Search(query, 15, null), loaded.Search(query, 15, null));
        }
    }

    private async Task<MediaItem> AddItemAsync(int number)
    {
        var item = bed.NewItem(number);
        await bed.Library.Items.AddAsync(item, Token);
        return item;
    }
}
