using MediaFileClassifier.Domain.Identity;

namespace MediaFileClassifier.Domain.Analysis;

/// <summary>
/// An AI model the library has used (E01-S05). Analysis runs and embeddings reference it, so changing the model
/// makes the affected items identifiable as needing re-indexing (spec 3.11).
/// </summary>
/// <remarks>
/// Invariants: the provider and model name are not blank, and the embedding dimension, when set, is positive.
/// A registration is immutable: a new version or a different model file gets a new registration.
/// </remarks>
public sealed class ModelRegistration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ModelRegistration"/> class.
    /// </summary>
    /// <param name="id">Registration ID.</param>
    /// <param name="providerId">Provider that runs the model, e.g. <c>onnx</c> or <c>openai-compatible</c>.</param>
    /// <param name="modelName">Model name as the provider knows it.</param>
    /// <param name="version">Model version, if the provider reports one.</param>
    /// <param name="fileHash">Hash of the local model file, if there is one.</param>
    /// <param name="embeddingDimension">Vector length, for embedding models.</param>
    /// <param name="registeredAt">When the model was first used in this library.</param>
    public ModelRegistration(
        Id<ModelRegistration> id,
        string providerId,
        string modelName,
        string? version,
        string? fileHash,
        int? embeddingDimension,
        DateTimeOffset registeredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        if (embeddingDimension is { } dimension)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension, nameof(embeddingDimension));
        }

        Id = id;
        ProviderId = providerId;
        ModelName = modelName;
        Version = version;
        FileHash = fileHash;
        EmbeddingDimension = embeddingDimension;
        RegisteredAt = registeredAt;
    }

    public Id<ModelRegistration> Id { get; }

    public string ProviderId { get; }

    public string ModelName { get; }

    public string? Version { get; }

    public string? FileHash { get; }

    public int? EmbeddingDimension { get; }

    public DateTimeOffset RegisteredAt { get; }

    public static ModelRegistration Create(
        string providerId,
        string modelName,
        string? version,
        string? fileHash,
        int? embeddingDimension,
        DateTimeOffset now) =>
        new(Id<ModelRegistration>.New(), providerId, modelName, version, fileHash, embeddingDimension, now);

    /// <summary>
    /// Checks whether this registration describes the given model, so an existing registration is reused instead of duplicated.
    /// </summary>
    /// <param name="providerId">Provider that runs the model.</param>
    /// <param name="modelName">Model name.</param>
    /// <param name="version">Model version, if known.</param>
    /// <param name="fileHash">Model file hash, if known.</param>
    /// <returns><c>true</c> if every identifying field matches.</returns>
    public bool Describes(string providerId, string modelName, string? version, string? fileHash) =>
        string.Equals(ProviderId, providerId, StringComparison.Ordinal)
        && string.Equals(ModelName, modelName, StringComparison.Ordinal)
        && string.Equals(Version, version, StringComparison.Ordinal)
        && string.Equals(FileHash, fileHash, StringComparison.OrdinalIgnoreCase);
}
