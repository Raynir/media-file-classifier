using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;

namespace MediaFileClassifier.Infrastructure.Media.Formats;

/// <summary>
/// WebP images: a still image with one frame, an animated image with more.
/// </summary>
public sealed class WebPFormatHandler : IMediaFormatHandler
{
    /// <inheritdoc />
    public string FormatId => "webp";

    /// <inheritdoc />
    public string MimeType => "image/webp";

    /// <inheritdoc />
    public IReadOnlyList<string> Extensions { get; } = [".webp"];

    /// <inheritdoc />
    public int SignatureLength => 12;

    /// <inheritdoc />
    public bool MatchesSignature(ReadOnlySpan<byte> header)
        => header.Length >= 12 && header.StartsWith("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8);

    /// <inheritdoc />
    public ValueTask<FormatInspection> InspectAsync(Stream content, CancellationToken cancellationToken)
        => ValueTask.FromResult(FormatInspection.Supported(MediaType.Image));
}
