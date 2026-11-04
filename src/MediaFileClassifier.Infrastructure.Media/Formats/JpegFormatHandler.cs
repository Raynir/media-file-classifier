using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Infrastructure.Media.Metadata;
using MediaFileClassifier.Infrastructure.Media.Thumbnails;

namespace MediaFileClassifier.Infrastructure.Media.Formats;

/// <summary>
/// JPEG images, recognised by the start-of-image marker followed by another marker.
/// </summary>
/// <param name="metadataExtractor">Reads the metadata.</param>
/// <param name="thumbnailGenerator">Makes the thumbnails.</param>
public sealed class JpegFormatHandler(ImageMetadataExtractor metadataExtractor, ImageThumbnailer thumbnailGenerator) : IMediaFormatHandler
{
    /// <inheritdoc />
    public string FormatId => "jpeg";

    /// <inheritdoc />
    public string MimeType => "image/jpeg";

    /// <inheritdoc />
    public IReadOnlyList<string> Extensions { get; } = [".jpg", ".jpeg"];

    /// <inheritdoc />
    public IMediaMetadataExtractor MetadataExtractor => metadataExtractor;

    /// <inheritdoc />
    public IThumbnailGenerator ThumbnailGenerator => thumbnailGenerator;

    /// <inheritdoc />
    public int SignatureLength => 3;

    /// <inheritdoc />
    public bool MatchesSignature(ReadOnlySpan<byte> header) => header.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]);

    /// <inheritdoc />
    public ValueTask<FormatInspection> InspectAsync(Stream content, CancellationToken cancellationToken)
        => ValueTask.FromResult(FormatInspection.Supported(MediaType.Image));
}
