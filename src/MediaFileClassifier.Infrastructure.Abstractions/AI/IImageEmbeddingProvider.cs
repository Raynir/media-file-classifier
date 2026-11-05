namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// Embeds images into a vector space shared with the <see cref="ITextEmbeddingProvider"/> of the same
/// <see cref="IAiProvider.Model"/>, so text queries can find images.
/// </summary>
public interface IImageEmbeddingProvider : IAiProvider
{
    /// <summary>
    /// Gets the vector length. It equals <see cref="ModelIdentity.EmbeddingDimension"/> of <see cref="IAiProvider.Model"/>.
    /// </summary>
    int Dimension { get; }

    /// <summary>
    /// Embeds a batch of images. Batching lets GPU providers run faster; a provider may split the batch.
    /// </summary>
    /// <param name="images">The images.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One L2-normalized vector of <see cref="Dimension"/> values per image, in the same order.</returns>
    /// <exception cref="AiProviderException">The call failed.</exception>
    Task<AiOutput<IReadOnlyList<ReadOnlyMemory<float>>>> EmbedImagesAsync(IReadOnlyList<AiImage> images, CancellationToken cancellationToken);
}
