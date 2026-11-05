using MediaFileClassifier.Domain.Analysis;

namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// Identifies the exact model that produced an output: the provider kind key, the model name, and the version or file
/// hash (E01-S05-T04). It becomes a <see cref="ModelRegistration"/> in each library, which analysis runs and
/// embeddings reference, so a model change makes the affected items identifiable as needing re-indexing.
/// </summary>
/// <remarks>
/// Two providers produce comparable embeddings only if their identities are equal. That's why the text tower of a
/// model reports the same identity as its image tower.
/// </remarks>
public sealed record ModelIdentity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ModelIdentity"/> class.
    /// </summary>
    /// <param name="providerId">Provider kind key, e.g. <see cref="ProviderIds.Onnx"/>.</param>
    /// <param name="modelName">Model name as the provider knows it.</param>
    /// <param name="version">Model version, if known.</param>
    /// <param name="fileHash">Hash of the model files, if they're local. Stored in lower case.</param>
    /// <param name="embeddingDimension">Vector length, for embedding models.</param>
    public ModelIdentity(string providerId, string modelName, string? version = null, string? fileHash = null, int? embeddingDimension = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        if (embeddingDimension is { } dimension)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension, nameof(embeddingDimension));
        }

        ProviderId = providerId;
        ModelName = modelName;
        Version = string.IsNullOrWhiteSpace(version) ? null : version;
        FileHash = string.IsNullOrWhiteSpace(fileHash) ? null : fileHash.ToLowerInvariant();
        EmbeddingDimension = embeddingDimension;
    }

    public string ProviderId { get; }

    public string ModelName { get; }

    public string? Version { get; }

    public string? FileHash { get; }

    public int? EmbeddingDimension { get; }

    /// <summary>
    /// Checks whether a library's registration describes this model.
    /// </summary>
    /// <param name="registration">The registration.</param>
    /// <returns><c>true</c> if every identifying field and the dimension match.</returns>
    public bool Matches(ModelRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        return registration.Describes(ProviderId, ModelName, Version, FileHash)
            && registration.EmbeddingDimension == EmbeddingDimension;
    }

    /// <summary>
    /// Creates a new registration of this model for a library.
    /// </summary>
    /// <param name="now">Current time.</param>
    /// <returns>The registration.</returns>
    public ModelRegistration ToRegistration(DateTimeOffset now) =>
        ModelRegistration.Create(ProviderId, ModelName, Version, FileHash, EmbeddingDimension, now);

    /// <inheritdoc />
    public override string ToString() =>
        Version is null ? $"{ProviderId}:{ModelName}" : $"{ProviderId}:{ModelName}@{Version}";
}
