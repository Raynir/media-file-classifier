namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// Where an item's date taken comes from: the first of these that the file has (E03-S03).
/// </summary>
public enum DateTakenSource
{
    /// <summary>
    /// The EXIF DateTimeOriginal of a photo.
    /// </summary>
    ExifDateTimeOriginal,

    /// <summary>
    /// The creation time in a video's container.
    /// </summary>
    VideoCreationTime,

    /// <summary>
    /// The file's last modification time.
    /// </summary>
    FileModified,
}
