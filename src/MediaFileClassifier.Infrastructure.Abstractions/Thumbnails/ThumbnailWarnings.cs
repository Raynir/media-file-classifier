namespace MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;

/// <summary>
/// The <see cref="Domain.Metadata.ExtractionWarning"/> codes of thumbnail generation. The import pipeline stores them
/// with the item's technical metadata warnings, and the item shows a placeholder.
/// </summary>
public static class ThumbnailWarnings
{
    /// <summary>
    /// The thumbnail couldn't be made, e.g. because the file can't be decoded. Detail: the error.
    /// </summary>
    public const string ThumbnailFailed = nameof(ThumbnailFailed);

    /// <summary>
    /// FFmpeg isn't available, so a video has no thumbnail (D068). Regenerating after FFmpeg is installed makes one.
    /// </summary>
    public const string ThumbnailFfmpegUnavailable = nameof(ThumbnailFfmpegUnavailable);
}
