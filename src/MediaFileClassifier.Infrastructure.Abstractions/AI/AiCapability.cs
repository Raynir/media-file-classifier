namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// An AI capability. Each one has its own interface, and a provider implements any subset of them.
/// </summary>
public enum AiCapability
{
    /// <summary>
    /// <see cref="IImageDescriber"/>: structured description of an image.
    /// </summary>
    ImageDescription,

    /// <summary>
    /// <see cref="IOcrProvider"/>: text found in an image.
    /// </summary>
    Ocr,

    /// <summary>
    /// <see cref="ITranscriptionProvider"/>: transcript of audio.
    /// </summary>
    Transcription,

    /// <summary>
    /// <see cref="IImageEmbeddingProvider"/>: image embedding.
    /// </summary>
    ImageEmbedding,

    /// <summary>
    /// <see cref="ITextEmbeddingProvider"/>: text embedding in the image model's space.
    /// </summary>
    TextEmbedding,
}
