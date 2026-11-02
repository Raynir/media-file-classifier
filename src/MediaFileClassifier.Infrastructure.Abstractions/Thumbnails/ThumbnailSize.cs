namespace MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;

/// <summary>
/// The thumbnail sizes, as the longest edge in pixels. Two sizes keep thumbnails sharp at every grid size on high-DPI
/// screens (E03-S04). Images smaller than a size are never upscaled.
/// </summary>
public enum ThumbnailSize
{
    /// <summary>
    /// 256 pixels on the longest edge.
    /// </summary>
    Small = 256,

    /// <summary>
    /// 512 pixels on the longest edge.
    /// </summary>
    Large = 512,
}
