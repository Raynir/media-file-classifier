using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Infrastructure.Media.Metadata;
using MediaFileClassifier.Infrastructure.Media.Thumbnails;

namespace MediaFileClassifier.Infrastructure.Media.Formats;

/// <summary>
/// GIF images: a still image with one frame, an animated image with more.
/// </summary>
/// <param name="metadataExtractor">Reads the metadata.</param>
/// <param name="thumbnailGenerator">Makes the thumbnails.</param>
public sealed class GifFormatHandler(ImageMetadataExtractor metadataExtractor, ImageThumbnailer thumbnailGenerator) : IMediaFormatHandler
{
    /// <inheritdoc />
    public string FormatId => "gif";

    /// <inheritdoc />
    public string MimeType => "image/gif";

    /// <inheritdoc />
    public IReadOnlyList<string> Extensions { get; } = [".gif"];

    /// <inheritdoc />
    public IMediaMetadataExtractor MetadataExtractor => metadataExtractor;

    /// <inheritdoc />
    public IThumbnailGenerator ThumbnailGenerator => thumbnailGenerator;

    /// <inheritdoc />
    public int SignatureLength => 6;

    /// <inheritdoc />
    public bool MatchesSignature(ReadOnlySpan<byte> header) => header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8);

    /// <inheritdoc />
    public async ValueTask<FormatInspection> InspectAsync(Stream content, CancellationToken cancellationToken)
    {
        var frames = await GifStructure.CountFramesAsync(content, limit: 2, cancellationToken);
        return FormatInspection.Supported(frames > 1 ? MediaType.AnimatedImage : MediaType.Image);
    }
}
