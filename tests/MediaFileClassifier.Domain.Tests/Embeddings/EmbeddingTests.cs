using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Domain.Tests.Embeddings;

/// <summary>
/// Tests for <see cref="Embedding"/>.
/// </summary>
public sealed class EmbeddingTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_TakesDimensionFromModel()
    {
        var model = ModelRegistration.Create("onnx", "clip-vit-b32", "1", null, 3, Now);

        var embedding = Embedding.Create(Id<MediaItem>.New(), model, EmbeddingKind.Image, [0.1f, 0.2f, 0.3f], Now);

        Assert.Equal(3, embedding.Dimension);
        Assert.Equal(model.Id, embedding.ModelId);
    }

    [Fact]
    public void Create_RejectsVectorOfWrongLength()
    {
        var model = ModelRegistration.Create("onnx", "clip-vit-b32", "1", null, 512, Now);

        Assert.Throws<ArgumentException>(() => Embedding.Create(Id<MediaItem>.New(), model, EmbeddingKind.Image, [0.1f], Now));
    }

    [Fact]
    public void Create_RejectsNonEmbeddingModel()
    {
        var model = ModelRegistration.Create("openai-compatible", "qwen2.5-vl", null, null, null, Now);

        Assert.Throws<ArgumentException>(() => Embedding.Create(Id<MediaItem>.New(), model, EmbeddingKind.Image, [0.1f], Now));
    }

    [Fact]
    public void VectorsOfDifferentModels_AreNeverComparable()
    {
        var oldModel = ModelRegistration.Create("onnx", "clip-vit-b32", "1", null, 2, Now);
        var newModel = ModelRegistration.Create("onnx", "siglip-base", "1", null, 2, Now);
        var itemId = Id<MediaItem>.New();
        var oldEmbedding = Embedding.Create(itemId, oldModel, EmbeddingKind.Image, [1f, 0f], Now);
        var newEmbedding = Embedding.Create(itemId, newModel, EmbeddingKind.Image, [1f, 0f], Now);

        Assert.False(oldEmbedding.IsComparableWith(newModel.Id));
        Assert.Throws<InvalidOperationException>(() => oldEmbedding.EnsureComparableWith(newEmbedding));
        oldEmbedding.EnsureComparableWith(oldEmbedding);
    }
}
