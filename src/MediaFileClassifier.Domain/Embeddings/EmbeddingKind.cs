namespace MediaFileClassifier.Domain.Embeddings;

/// <summary>
/// What an embedding was computed from.
/// </summary>
public enum EmbeddingKind
{
    /// <summary>
    /// The pixels of the image, or of representative frames of a video or animation.
    /// </summary>
    Image,

    /// <summary>
    /// Text derived from the item, such as its description or transcript.
    /// </summary>
    DerivedText,
}
