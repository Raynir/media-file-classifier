using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Media.Metadata;

namespace MediaFileClassifier.Infrastructure.Media.Formats;

/// <summary>
/// MP4 video. Any video track makes a file a video, whatever its codec: H.264, HEVC and AV1 are the common ones, and
/// whether FFmpeg can decode a file shows later, when its metadata and frames are read. Audio-only files and the
/// relatives that share the container (QuickTime, 3GPP, HEIF) are skipped (D010).
/// </summary>
/// <param name="metadataExtractor">Reads the metadata.</param>
public sealed class Mp4FormatHandler(FileMetadataExtractor metadataExtractor) : IMediaFormatHandler
{
    /// <summary>
    /// Major brands of ISO base media files that aren't MP4 video, by prefix.
    /// </summary>
    private static readonly string[] UnsupportedBrandPrefixes =
    [
        "qt",   // QuickTime
        "3gp",  // 3GPP
        "3g2",  // 3GPP2
        "heic", // HEIF image and sequence brands
        "heix",
        "heim",
        "heis",
        "hevc",
        "hevx",
        "mif1",
        "msf1",
        "avif", // AVIF image and sequence
        "avis",
        "crx",  // Canon raw
    ];

    /// <inheritdoc />
    public string FormatId => "mp4";

    /// <inheritdoc />
    public string MimeType => "video/mp4";

    /// <inheritdoc />
    public IReadOnlyList<string> Extensions { get; } = [".mp4", ".m4v"];

    /// <inheritdoc />
    public IMediaMetadataExtractor MetadataExtractor => metadataExtractor;

    /// <inheritdoc />
    public int SignatureLength => 8;

    /// <inheritdoc />
    public bool MatchesSignature(ReadOnlySpan<byte> header) => header.Length >= 8 && header[4..8].SequenceEqual("ftyp"u8);

    /// <inheritdoc />
    public async ValueTask<FormatInspection> InspectAsync(Stream content, CancellationToken cancellationToken)
    {
        var summary = await Mp4Structure.ReadAsync(content, cancellationToken);
        if (summary.MajorBrand is { } brand
            && UnsupportedBrandPrefixes.Any(prefix => brand.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return FormatInspection.Rejected(new(MediaFormatIssues.UnsupportedContainer, brand.TrimEnd()));
        }

        if (summary.Tracks is null)
        {
            return FormatInspection.Rejected(new(MediaFormatIssues.DamagedFile, summary.Problem));
        }

        return summary.Tracks.Any(track => track.HandlerType == "vide")
            ? FormatInspection.Supported(MediaType.Video)
            : FormatInspection.Rejected(new(MediaFormatIssues.NoVideoTrack));
    }
}
