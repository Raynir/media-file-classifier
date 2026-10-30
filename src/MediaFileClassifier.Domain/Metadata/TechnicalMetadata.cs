using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// Facts read from the file itself (spec 1.3, step 2). One per media item, replaced whenever the file is re-read.
/// </summary>
/// <remarks>
/// Fields used for filtering and sorting are typed properties. Everything the reader found, including the full EXIF,
/// is kept as JSON in <see cref="RawMetadataJson"/> for later use. A value is <c>null</c> when the file doesn't have it.
/// Descriptive values found in the file, such as an embedded title or keywords, are stored as technical-source
/// descriptive values and tags instead, with the Technical source.
/// </remarks>
public sealed record TechnicalMetadata
{
    public required Id<MediaItem> ItemId { get; init; }

    /// <summary>
    /// Gets the stored pixel width, before <see cref="Orientation"/> is applied.
    /// </summary>
    public int? Width { get; init; }

    /// <summary>
    /// Gets the stored pixel height, before <see cref="Orientation"/> is applied.
    /// </summary>
    public int? Height { get; init; }

    public ImageOrientation Orientation { get; init; } = ImageOrientation.Normal;

    /// <summary>
    /// Gets the playback length of a video or animated image.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// Gets the number of frames of an animated image.
    /// </summary>
    public int? FrameCount { get; init; }

    public double? FrameRate { get; init; }

    /// <summary>
    /// Gets the format detected from the content, such as <c>jpeg</c> or <c>mp4</c>. It can differ from the file extension.
    /// </summary>
    public string? ContainerFormat { get; init; }

    public string? MimeType { get; init; }

    public string? VideoCodec { get; init; }

    public string? AudioCodec { get; init; }

    public long? BitRate { get; init; }

    /// <summary>
    /// Gets the local wall-clock time the photo or video was taken (EXIF DateTimeOriginal), without a time zone.
    /// </summary>
    public DateTime? DateTaken { get; init; }

    /// <summary>
    /// Gets the UTC offset of <see cref="DateTaken"/>, when the file records it.
    /// </summary>
    public TimeSpan? DateTakenOffset { get; init; }

    public string? CameraMake { get; init; }

    public string? CameraModel { get; init; }

    public string? LensModel { get; init; }

    public GeoLocation? Location { get; init; }

    /// <summary>
    /// Gets everything the metadata reader found, as JSON.
    /// </summary>
    public string? RawMetadataJson { get; init; }

    public required DateTimeOffset ExtractedAt { get; init; }

    /// <summary>
    /// Gets the width as displayed, with <see cref="Orientation"/> applied.
    /// </summary>
    public int? DisplayWidth => SwapsAxes ? Height : Width;

    /// <summary>
    /// Gets the height as displayed, with <see cref="Orientation"/> applied.
    /// </summary>
    public int? DisplayHeight => SwapsAxes ? Width : Height;

    /// <summary>
    /// Gets the number of pixels, for the resolution filter.
    /// </summary>
    public long? PixelCount => Width is { } width && Height is { } height ? (long)width * height : null;

    private bool SwapsAxes => Orientation >= ImageOrientation.Transpose;
}
