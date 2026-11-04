using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Abstractions.Search;

/// <summary>
/// The vector side of a query. Only vectors of the same model are compared (D067): a query from one model never
/// scores another model's embeddings.
/// </summary>
public sealed record SearchVector
{
    private SearchVector(Id<ModelRegistration> modelId, ReadOnlyMemory<float> vector, Id<MediaItem>? itemId)
    {
        ModelId = modelId;
        Vector = vector;
        ItemId = itemId;
    }

    /// <summary>
    /// Gets the model whose vector space the query is in.
    /// </summary>
    public Id<ModelRegistration> ModelId { get; }

    /// <summary>
    /// Gets the query vector; empty for <see cref="FromItem"/>.
    /// </summary>
    public ReadOnlyMemory<float> Vector { get; }

    /// <summary>
    /// Gets the item to find similar items to, or <c>null</c>.
    /// </summary>
    public Id<MediaItem>? ItemId { get; }

    /// <summary>
    /// Creates a query from a vector, such as an encoded text query or reference image. It's L2-normalised here, so
    /// the dot product is the cosine similarity.
    /// </summary>
    /// <param name="modelId">The model that produced the vector.</param>
    /// <param name="vector">The vector. It's copied.</param>
    /// <returns>The query vector.</returns>
    /// <exception cref="ArgumentException">The vector is empty, zero, or has a NaN or infinite value.</exception>
    public static SearchVector FromVector(Id<ModelRegistration> modelId, ReadOnlySpan<float> vector)
    {
        if (vector.IsEmpty)
        {
            throw new ArgumentException("The vector is empty.", nameof(vector));
        }

        var sum = 0.0;
        foreach (var value in vector)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentException("The vector has a NaN or infinite value.", nameof(vector));
            }

            sum += (double)value * value;
        }

        if (sum == 0)
        {
            throw new ArgumentException("The vector is zero.", nameof(vector));
        }

        var norm = (float)Math.Sqrt(sum);
        var normalised = new float[vector.Length];
        for (var i = 0; i < vector.Length; i++)
        {
            normalised[i] = vector[i] / norm;
        }

        return new SearchVector(modelId, normalised, null);
    }

    /// <summary>
    /// Creates a "find similar" query from an indexed item's own embedding of a model: its whole-item vector, or its
    /// first keyframe's. The item itself is left out of the results. An item without embeddings of the model finds
    /// nothing by vector.
    /// </summary>
    /// <param name="modelId">The model.</param>
    /// <param name="itemId">The item.</param>
    /// <returns>The query vector.</returns>
    public static SearchVector FromItem(Id<ModelRegistration> modelId, Id<MediaItem> itemId) => new(modelId, ReadOnlyMemory<float>.Empty, itemId);
}
