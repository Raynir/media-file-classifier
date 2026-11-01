using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Media.Metadata;

namespace MediaFileClassifier.Infrastructure.Media.Formats;

/// <summary>
/// WebP images: a still image with one frame, an animated image with more.
/// </summary>
/// <param name="metadataExtractor">Reads the metadata.</param>
public sealed class WebPFormatHandler(ImageMetadataExtractor metadataExtractor) : IMediaFormatHandler
{
    /// <inheritdoc />
    public string FormatId => "webp";

    /// <inheritdoc />
    public string MimeType => "image/webp";

    /// <inheritdoc />
    public IReadOnlyList<string> Extensions { get; } = [".webp"];

    /// <inheritdoc />
    public IMediaMetadataExtractor MetadataExtractor => metadataExtractor;

    /// <inheritdoc />
    public int SignatureLength => 12;

    /// <inheritdoc />
    public bool MatchesSignature(ReadOnlySpan<byte> header)
        => header.Length >= 12 && header.StartsWith("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8);

    /// <inheritdoc />
    public async ValueTask<FormatInspection> InspectAsync(Stream content, CancellationToken cancellationToken)
    {
        var frames = await WebPStructure.CountFramesAsync(content, limit: 2, cancellationToken);
        return FormatInspection.Supported(frames > 1 ? MediaType.AnimatedImage : MediaType.Image);
    }
}
