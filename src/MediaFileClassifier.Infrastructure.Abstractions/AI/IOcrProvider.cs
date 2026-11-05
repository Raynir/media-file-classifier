namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// Reads text found in an image (OCR).
/// </summary>
public interface IOcrProvider : IAiProvider
{
    /// <summary>
    /// Reads the text in one image.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The text found, possibly empty.</returns>
    /// <exception cref="AiProviderException">The call failed.</exception>
    Task<AiOutput<OcrResult>> RecognizeTextAsync(AiImage image, CancellationToken cancellationToken);
}
