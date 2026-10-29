namespace MediaFileClassifier.Domain.Media;

/// <summary>
/// Kind of media an item holds.
/// </summary>
public enum MediaType
{
    /// <summary>
    /// A still image.
    /// </summary>
    Image,

    /// <summary>
    /// An image with more than one frame, such as an animated GIF or WebP.
    /// </summary>
    AnimatedImage,

    /// <summary>
    /// A video file.
    /// </summary>
    Video,
}
