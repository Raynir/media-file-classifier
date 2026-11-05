namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// Embeds text, such as search queries, into the same vector space as the images of an
/// <see cref="IImageEmbeddingProvider"/>. The two share a space only if their <see cref="IAiProvider.Model"/>
/// identities are equal: vectors of different models are never compared. The text tower of a two-tower model such as
/// SigLIP 2 therefore reports the same identity as its image tower.
/// </summary>
public interface ITextEmbeddingProvider : IAiProvider
{
    /// <summary>
    /// Gets the vector length. It equals <see cref="ModelIdentity.EmbeddingDimension"/> of <see cref="IAiProvider.Model"/>.
    /// </summary>
    int Dimension { get; }

    /// <summary>
    /// Embeds a batch of texts. The provider applies its model's text preparation, such as lower-casing for
    /// SigLIP 2 (D092).
    /// </summary>
    /// <param name="texts">The texts.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One L2-normalized vector of <see cref="Dimension"/> values per text, in the same order.</returns>
    /// <exception cref="AiProviderException">The call failed.</exception>
    Task<AiOutput<IReadOnlyList<ReadOnlyMemory<float>>>> EmbedTextsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}
