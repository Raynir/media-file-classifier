using System.Numerics.Tensors;
using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.Search.Vectors;

namespace MediaFileClassifier.Infrastructure.Tests.Search;

/// <summary>
/// Tests for the in-memory vector index (E01-S04-T03): top-K results against a brute-force reference, model isolation,
/// filters, and updates.
/// </summary>
public sealed class VectorIndexTests
{
    private const int Dimension = 64;
    private static readonly Id<ModelRegistration> ModelA = Id<ModelRegistration>.New();
    private static readonly Id<ModelRegistration> ModelB = Id<ModelRegistration>.New();

    [Theory]
    [InlineData(300, 1, 10)]
    [InlineData(1000, 3, 20)]
    [InlineData(5000, 3, 50)]
    public void TopK_EqualsBruteForce_OnTheStoredValues(int items, int maxVectorsPerItem, int k)
    {
        var data = SyntheticItems.Create(items, maxVectorsPerItem, Dimension, seed: items);
        using var index = Build(data);
        var random = new Random(7);

        for (var q = 0; q < 20; q++)
        {
            var search = SearchVector.FromVector(ModelA, SyntheticItems.RandomUnit(random, Dimension));
            var query = search.Vector.ToArray();

            var actual = index.Search(search, k, mask: null);

            var expected = BruteForce(data, query, k, roundToHalf: true, mask: null);
            Assert.Equal(expected.Select(hit => hit.Document), actual.Select(hit => hit.DocumentId));
            Assert.Equal(expected.Select(hit => hit.Score), actual.Select(hit => hit.Similarity));
        }
    }

    [Fact]
    public void HalfPrecision_KeepsRecallAgainstFloat32()
    {
        var data = SyntheticItems.Create(3000, 3, Dimension, seed: 11);
        using var index = Build(data);
        var random = new Random(3);
        double found = 0;
        double wanted = 0;

        for (var q = 0; q < 50; q++)
        {
            var query = SyntheticItems.RandomUnit(random, Dimension);
            var exact = BruteForce(data, query, 20, roundToHalf: false, mask: null).Select(hit => hit.Document).ToHashSet();
            var actual = index.Search(SearchVector.FromVector(ModelA, query), 20, mask: null);
            found += actual.Count(hit => exact.Contains(hit.DocumentId));
            wanted += exact.Count;
        }

        Assert.True(found / wanted >= 0.99, $"Recall@20 was {found / wanted:F4}.");
    }

    [Fact]
    public void Mask_LimitsResultsToTheGivenDocuments()
    {
        var data = SyntheticItems.Create(500, 2, Dimension, seed: 5);
        using var index = Build(data);
        var mask = new bool[data.Max(item => item.Document) + 1];
        foreach (var item in data.Where(item => item.Document % 3 == 0))
        {
            mask[item.Document] = true;
        }

        var search = SearchVector.FromVector(ModelA, SyntheticItems.RandomUnit(new Random(1), Dimension));
        var query = search.Vector.ToArray();

        var actual = index.Search(search, 15, mask);

        Assert.Equal(BruteForce(data, query, 15, roundToHalf: true, mask).Select(hit => hit.Document), actual.Select(hit => hit.DocumentId));
        Assert.All(actual, hit => Assert.Equal(0, hit.DocumentId % 3));
    }

    [Fact]
    public void Query_ComparesOnlyTheSameModel()
    {
        using var index = new VectorIndex();
        var itemA = Id<MediaItem>.New();
        var itemB = Id<MediaItem>.New();
        var vector = SyntheticItems.RandomUnit(new Random(1), Dimension);
        index.Apply([Committed(1, itemA, ModelA, vector), Committed(2, itemB, ModelB, vector)]);

        Assert.Equal([itemA], index.Search(SearchVector.FromVector(ModelA, vector), 10, null).Select(hit => hit.ItemId));
        Assert.Equal([itemB], index.Search(SearchVector.FromVector(ModelB, vector), 10, null).Select(hit => hit.ItemId));
        Assert.Empty(index.Search(SearchVector.FromVector(Id<ModelRegistration>.New(), vector), 10, null));
    }

    [Fact]
    public void Query_WithAnotherDimension_Throws()
    {
        using var index = new VectorIndex();
        index.Apply([Committed(1, Id<MediaItem>.New(), ModelA, SyntheticItems.RandomUnit(new Random(1), Dimension))]);

        Assert.Throws<ArgumentException>(() => index.Search(SearchVector.FromVector(ModelA, new float[Dimension + 1].Select(_ => 1f).ToArray()), 5, null));
    }

    [Fact]
    public void FindSimilar_StartsFromTheItemsFirstVector_AndLeavesTheItemOut()
    {
        var data = SyntheticItems.Create(400, 3, Dimension, seed: 9);
        using var index = Build(data);
        var start = data[17];

        var actual = index.Search(SearchVector.FromItem(ModelA, start.ItemId), 10, null);

        var expected = BruteForce(data.Where(item => item != start).ToList(), RoundToHalf(start.Vectors[0]), 10, roundToHalf: true, mask: null);
        Assert.Equal(expected.Select(hit => hit.Document), actual.Select(hit => hit.DocumentId));
        Assert.DoesNotContain(actual, hit => hit.ItemId == start.ItemId);
    }

    [Fact]
    public void FindSimilar_ItemWithoutVectors_FindsNothing()
    {
        using var index = Build(SyntheticItems.Create(50, 1, Dimension, seed: 2));

        Assert.Empty(index.Search(SearchVector.FromItem(ModelA, Id<MediaItem>.New()), 10, null));
    }

    [Fact]
    public void ReplacingAndRemovingManyItems_KeepsResultsExact()
    {
        var data = SyntheticItems.Create(2000, 2, Dimension, seed: 21);
        using var index = Build(data);
        var random = new Random(4);

        // Replace every item twice and remove a third of them: enough dead vectors to compact several times.
        for (var round = 0; round < 2; round++)
        {
            for (var i = 0; i < data.Count; i++)
            {
                data[i] = data[i] with { Vectors = [.. Enumerable.Range(0, random.Next(1, 4)).Select(_ => SyntheticItems.RandomUnit(random, Dimension))] };
                index.Apply([Committed(data[i])]);
            }
        }

        var removed = data.Where(item => item.Document % 3 == 0).ToList();
        index.Apply([.. removed.Select(item => new CommittedEmbeddings(item.Document, new ItemEmbeddings(item.ItemId, ModelA, [])))]);
        data.RemoveAll(removed.Contains);

        var search = SearchVector.FromVector(ModelA, SyntheticItems.RandomUnit(random, Dimension));
        var query = search.Vector.ToArray();
        var actual = index.Search(search, 30, null);

        Assert.Equal(BruteForce(data, query, 30, roundToHalf: true, mask: null).Select(hit => hit.Document), actual.Select(hit => hit.DocumentId));
        Assert.Equal(data.Sum(item => item.Vectors.Count), index.VectorCount);

        // The running mean behind the relevance cut follows replacements, removals and compaction.
        var mean = data.Average(item => (double)TensorPrimitives.Dot(RoundToHalf(item.Vectors[0]), query));
        Assert.Equal(mean, index.MeanSimilarity(search), 4);
    }

    [Fact]
    public void MeanSimilarity_IsTheMeanOfTheFirstVectors()
    {
        var data = SyntheticItems.Create(300, 3, Dimension, seed: 5);
        using var index = Build(data);
        var query = SearchVector.FromVector(ModelA, SyntheticItems.RandomUnit(new Random(9), Dimension));
        var expected = data.Average(item => (double)TensorPrimitives.Dot(RoundToHalf(item.Vectors[0]), query.Vector.Span));

        Assert.Equal(expected, index.MeanSimilarity(query), 4);
        Assert.Equal(0, index.MeanSimilarity(SearchVector.FromVector(ModelB, query.Vector.Span)));
        Assert.Equal(0, index.MeanSimilarity(SearchVector.FromItem(ModelA, Id<MediaItem>.New())));
    }

    internal static VectorIndex Build(IReadOnlyList<SyntheticItem> data)
    {
        var index = new VectorIndex();
        index.Apply([.. data.Select(Committed)]);
        return index;
    }

    internal static List<(int Document, float Score)> BruteForce(IReadOnlyList<SyntheticItem> data, float[] query, int k, bool roundToHalf, bool[]? mask) =>
        [.. data
            .Where(item => mask is null || (item.Document < mask.Length && mask[item.Document]))
            .Select(item => (item.Document, Score: item.Vectors.Max(vector => TensorPrimitives.Dot(roundToHalf ? RoundToHalf(vector) : vector, query))))
            .OrderByDescending(hit => hit.Score)
            .ThenBy(hit => hit.Document)
            .Take(k)];

    private static float[] RoundToHalf(float[] vector) => [.. vector.Select(value => (float)(Half)value)];

    private static CommittedEmbeddings Committed(SyntheticItem item) =>
        new(item.Document, new ItemEmbeddings(
            item.ItemId,
            ModelA,
            [.. item.Vectors.Select(vector => new Embedding(Id<Embedding>.New(), item.ItemId, ModelA, EmbeddingKind.Image, null, Dimension, vector, DateTimeOffset.UnixEpoch))]));

    private static CommittedEmbeddings Committed(int document, Id<MediaItem> itemId, Id<ModelRegistration> modelId, float[] vector) =>
        new(document, new ItemEmbeddings(itemId, modelId, [new Embedding(Id<Embedding>.New(), itemId, modelId, EmbeddingKind.Image, null, vector.Length, vector, DateTimeOffset.UnixEpoch)]));
}

/// <summary>
/// An item with its vectors, for tests.
/// </summary>
/// <param name="Document">Document ID.</param>
/// <param name="ItemId">Item ID.</param>
/// <param name="Vectors">L2-normalised vectors.</param>
internal sealed record SyntheticItem(int Document, Id<MediaItem> ItemId, IReadOnlyList<float[]> Vectors);

/// <summary>
/// Makes clustered, L2-normalised test vectors, so nearest neighbours are meaningful.
/// </summary>
internal static class SyntheticItems
{
    public static List<SyntheticItem> Create(int count, int maxVectorsPerItem, int dimension, int seed)
    {
        var random = new Random(seed);
        var centres = Enumerable.Range(0, 32).Select(_ => RandomUnit(random, dimension)).ToArray();
        var items = new List<SyntheticItem>(count);
        for (var i = 0; i < count; i++)
        {
            var centre = centres[random.Next(centres.Length)];
            var vectors = Enumerable.Range(0, random.Next(1, maxVectorsPerItem + 1))
                .Select(_ => Normalise([.. centre.Select(value => value + ((float)random.NextDouble() - 0.5f) * 0.3f)]))
                .ToList();
            items.Add(new SyntheticItem(i + 1, Id<MediaItem>.New(), vectors));
        }

        return items;
    }

    public static float[] RandomUnit(Random random, int dimension) =>
        Normalise([.. Enumerable.Range(0, dimension).Select(_ => (float)(random.NextDouble() * 2 - 1))]);

    private static float[] Normalise(float[] vector)
    {
        var norm = TensorPrimitives.Norm(vector);
        return [.. vector.Select(value => value / norm)];
    }
}
