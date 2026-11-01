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

    /// <summary>
    /// Gets how many times an animated image repeats, as stored in the file: <c>0</c> means forever, and <c>null</c>
    /// means the file doesn't say, so it plays once.
    /// </summary>
    public int? LoopCount { get; init; }

    public double? FrameRate { get; init; }

    /// <summary>
    /// Gets the format detected from the content, such as <c>jpeg</c> or <c>mp4</c>. It can differ from the file extension.
    /// </summary>
    public string? ContainerFormat { get; init; }

    public string? MimeType { get; init; }

    public string? VideoCodec { get; init; }

    public string? AudioCodec { get; init; }

    /// <summary>
    /// Gets whether a video has an audio track, or <c>null</c> when it wasn't read.
    /// </summary>
    public bool? HasAudio { get; init; }

    /// <summary>
    /// Gets the color space, such as <c>sRGB</c>, an ICC profile name such as <c>Display P3</c>, or a video's
    /// <c>bt709</c>.
    /// </summary>
    public string? ColorSpace { get; init; }

    public long? BitRate { get; init; }

    /// <summary>
    /// Gets when the photo or video was taken, as a wall-clock time. With <see cref="DateTakenOffset"/> it is an exact
    /// instant; without, it is the local time where it was taken, in an unknown time zone (usual for EXIF).
    /// </summary>
    public DateTime? DateTaken { get; init; }

    /// <summary>
    /// Gets the UTC offset of <see cref="DateTaken"/>, or <c>null</c> when it is unknown. Video creation times and file
    /// modification times are instants, stored as UTC with a zero offset.
    /// </summary>
    public TimeSpan? DateTakenOffset { get; init; }

    /// <summary>
    /// Gets where <see cref="DateTaken"/> comes from, or <c>null</c> when there is no date.
    /// </summary>
    public DateTakenSource? DateTakenSource { get; init; }

    public string? CameraMake { get; init; }

    public string? CameraModel { get; init; }

    public string? LensModel { get; init; }

    public GeoLocation? Location { get; init; }

    /// <summary>
    /// Gets everything the metadata reader found, as JSON.
    /// </summary>
    public string? RawMetadataJson { get; init; }

    /// <summary>
    /// Gets what went wrong while reading the file. The values that could be read are still set.
    /// </summary>
    public IReadOnlyList<ExtractionWarning> Warnings { get; init; } = [];

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
