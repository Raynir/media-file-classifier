using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Domain.Embeddings;

/// <summary>
/// A semantic vector of an item, produced by one embedding model.
/// </summary>
/// <remarks>
/// <para>
/// <b>Vectors from different models are never compared.</b> Each model has its own vector space, and even models with
/// the same dimension produce unrelated numbers. Search only compares vectors with the same <see cref="ModelId"/>;
/// use <see cref="IsComparableWith(Id{ModelRegistration})"/> or <see cref="EnsureComparableWith"/>.
/// </para>
/// <para>
/// An item can hold several embeddings: one per model, kind and keyframe, e.g. from an old and a new model during a
/// migration (E06-S07-T05), or one per sampled frame of a video (E06-S02). Embeddings aren't versioned per analysis run, and rejecting a run in review keeps them (D021).
/// </para>
/// <para>Invariants: the dimension is positive and equals the vector length, and the vector has no NaN or infinite values.</para>
/// </remarks>
public sealed class Embedding
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Embedding"/> class.
    /// </summary>
    /// <param name="id">Embedding ID.</param>
    /// <param name="itemId">Embedded item.</param>
    /// <param name="modelId">Model that produced the vector.</param>
    /// <param name="kind">What the vector was computed from.</param>
    /// <param name="keyframeId">Frame the vector was computed from, or <c>null</c> for the whole item.</param>
    /// <param name="dimension">Vector length.</param>
    /// <param name="vector">The vector.</param>
    /// <param name="createdAt">When the vector was computed.</param>
    public Embedding(
        Id<Embedding> id,
        Id<MediaItem> itemId,
        Id<ModelRegistration> modelId,
        EmbeddingKind kind,
        Id<Keyframe>? keyframeId,
        int dimension,
        ReadOnlyMemory<float> vector,
        DateTimeOffset createdAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);
        if (vector.Length != dimension)
        {
            throw new ArgumentException($"The vector has {vector.Length} values, but the dimension is {dimension}.", nameof(vector));
        }

        foreach (var value in vector.Span)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentException("The vector has a NaN or infinite value.", nameof(vector));
            }
        }

        Id = id;
        ItemId = itemId;
        ModelId = modelId;
        Kind = kind;
        KeyframeId = keyframeId;
        Dimension = dimension;
        Vector = vector;
        CreatedAt = createdAt;
    }

    public Id<Embedding> Id { get; }

    public Id<MediaItem> ItemId { get; }

    public Id<ModelRegistration> ModelId { get; }

    public EmbeddingKind Kind { get; }

    public Id<Keyframe>? KeyframeId { get; }

    public int Dimension { get; }

    public ReadOnlyMemory<float> Vector { get; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Creates an embedding, checking the vector against the model's registered dimension.
    /// </summary>
    /// <param name="itemId">Embedded item.</param>
    /// <param name="model">Model that produced the vector.</param>
    /// <param name="kind">What the vector was computed from.</param>
    /// <param name="vector">The vector. It's copied.</param>
    /// <param name="now">Current time.</param>
    /// <param name="keyframeId">Frame the vector was computed from, or <c>null</c> for the whole item.</param>
    /// <returns>The embedding.</returns>
    public static Embedding Create(
        Id<MediaItem> itemId,
        ModelRegistration model,
        EmbeddingKind kind,
        ReadOnlySpan<float> vector,
        DateTimeOffset now,
        Id<Keyframe>? keyframeId = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.EmbeddingDimension is not { } dimension)
        {
            throw new ArgumentException($"Model {model.ModelName} isn't registered as an embedding model.", nameof(model));
        }

        return new Embedding(Id<Embedding>.New(), itemId, model.Id, kind, keyframeId, dimension, vector.ToArray(), now);
    }

    /// <summary>
    /// Checks whether a vector from the given model, such as a search query, can be compared with this one.
    /// </summary>
    /// <param name="modelId">Model of the other vector.</param>
    /// <returns><c>true</c> only for the same model.</returns>
    public bool IsComparableWith(Id<ModelRegistration> modelId) => ModelId == modelId;

    /// <summary>
    /// Guards a comparison with another embedding.
    /// </summary>
    /// <param name="other">The other embedding.</param>
    /// <exception cref="InvalidOperationException">The embeddings come from different models.</exception>
    public void EnsureComparableWith(Embedding other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (!IsComparableWith(other.ModelId))
        {
            throw new InvalidOperationException($"Embeddings of models {ModelId} and {other.ModelId} can't be compared.");
        }
    }
}
