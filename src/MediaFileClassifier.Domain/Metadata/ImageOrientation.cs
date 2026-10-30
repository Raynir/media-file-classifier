namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// How stored pixels must be transformed for display. Values match the EXIF Orientation tag.
/// </summary>
public enum ImageOrientation
{
    /// <summary>
    /// No transformation.
    /// </summary>
    Normal = 1,

    /// <summary>
    /// Mirrored horizontally.
    /// </summary>
    FlipHorizontal = 2,

    /// <summary>
    /// Rotated 180 degrees.
    /// </summary>
    Rotate180 = 3,

    /// <summary>
    /// Mirrored vertically.
    /// </summary>
    FlipVertical = 4,

    /// <summary>
    /// Mirrored along the top-left to bottom-right diagonal.
    /// </summary>
    Transpose = 5,

    /// <summary>
    /// Rotated 90 degrees clockwise.
    /// </summary>
    Rotate90 = 6,

    /// <summary>
    /// Mirrored along the top-right to bottom-left diagonal.
    /// </summary>
    Transverse = 7,

    /// <summary>
    /// Rotated 270 degrees clockwise.
    /// </summary>
    Rotate270 = 8,
}
