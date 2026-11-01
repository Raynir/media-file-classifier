namespace MediaFileClassifier.Infrastructure.Abstractions.Metadata;

/// <summary>
/// The <see cref="Domain.Metadata.ExtractionWarning"/> codes of the metadata extractors.
/// </summary>
public static class MetadataWarnings
{
    /// <summary>
    /// The file's metadata couldn't be read at all; only file-level facts are set. Detail: the error.
    /// </summary>
    public const string MetadataUnreadable = nameof(MetadataUnreadable);

    /// <summary>
    /// Part of the metadata is damaged; the rest was read. Detail: the reader's error.
    /// </summary>
    public const string MetadataPartlyUnreadable = nameof(MetadataPartlyUnreadable);

    /// <summary>
    /// The date taken in the file isn't a valid date, so the next date in the fallback chain is used. Detail: the stored text.
    /// </summary>
    public const string InvalidDateTaken = nameof(InvalidDateTaken);

    /// <summary>
    /// The GPS position in the file is out of range, so it is left out. Detail: the stored position.
    /// </summary>
    public const string InvalidGpsPosition = nameof(InvalidGpsPosition);

    /// <summary>
    /// FFmpeg isn't available, so a video has file-level metadata only (E01-S07-T03). Re-reading after FFmpeg is
    /// installed fills in the rest.
    /// </summary>
    public const string FfmpegUnavailable = nameof(FfmpegUnavailable);

    /// <summary>
    /// FFprobe ran but couldn't read the video. Detail: its error.
    /// </summary>
    public const string VideoProbeFailed = nameof(VideoProbeFailed);
}
