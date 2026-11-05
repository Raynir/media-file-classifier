namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// Helpers for AI providers.
/// </summary>
public static class AiProviderExtensions
{
    /// <summary>
    /// Lists the capabilities a provider implements.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <returns>The capabilities, in <see cref="AiCapability"/> order.</returns>
    public static IReadOnlyList<AiCapability> GetCapabilities(this IAiProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var capabilities = new List<AiCapability>();
        if (provider is IImageDescriber)
        {
            capabilities.Add(AiCapability.ImageDescription);
        }

        if (provider is IOcrProvider)
        {
            capabilities.Add(AiCapability.Ocr);
        }

        if (provider is ITranscriptionProvider)
        {
            capabilities.Add(AiCapability.Transcription);
        }

        if (provider is IImageEmbeddingProvider)
        {
            capabilities.Add(AiCapability.ImageEmbedding);
        }

        if (provider is ITextEmbeddingProvider)
        {
            capabilities.Add(AiCapability.TextEmbedding);
        }

        return capabilities;
    }

    /// <summary>
    /// Embeds one image.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <param name="image">The image.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The vector.</returns>
    public static async Task<AiOutput<ReadOnlyMemory<float>>> EmbedImageAsync(
        this IImageEmbeddingProvider provider,
        AiImage image,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var output = await provider.EmbedImagesAsync([image], cancellationToken).ConfigureAwait(false);
        return new AiOutput<ReadOnlyMemory<float>>(output.Value[0], output.ProviderId, output.Model, output.PromptVersion, output.Duration);
    }

    /// <summary>
    /// Embeds one text, such as a search query.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <param name="text">The text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The vector.</returns>
    public static async Task<AiOutput<ReadOnlyMemory<float>>> EmbedTextAsync(
        this ITextEmbeddingProvider provider,
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var output = await provider.EmbedTextsAsync([text], cancellationToken).ConfigureAwait(false);
        return new AiOutput<ReadOnlyMemory<float>>(output.Value[0], output.ProviderId, output.Model, output.PromptVersion, output.Duration);
    }
}
