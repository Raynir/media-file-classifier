namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// Tells quickly whether an image has text in it, without reading it. A provider that implements it lets the OCR
/// analyzer skip a slower provider on a picture with no text (E06-S05-T02).
/// </summary>
public interface ITextDetector
{
    /// <summary>
    /// Looks for text in one image.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if the image has at least one text region.</returns>
    /// <exception cref="AiProviderException">The call failed, for example because the model isn't installed.</exception>
    Task<bool> HasTextAsync(AiImage image, CancellationToken cancellationToken);
}
