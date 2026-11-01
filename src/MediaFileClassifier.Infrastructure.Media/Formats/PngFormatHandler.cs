using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;

namespace MediaFileClassifier.Infrastructure.Media.Formats;

/// <summary>
/// PNG images. Animated PNG isn't an MVP format (D010), so an APNG counts as a still image of its first frame.
/// </summary>
public sealed class PngFormatHandler : IMediaFormatHandler
{
    /// <inheritdoc />
    public string FormatId => "png";

    /// <inheritdoc />
    public string MimeType => "image/png";

    /// <inheritdoc />
    public IReadOnlyList<string> Extensions { get; } = [".png"];

    /// <inheritdoc />
    public int SignatureLength => 8;

    /// <inheritdoc />
    public bool MatchesSignature(ReadOnlySpan<byte> header)
        => header.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

    /// <inheritdoc />
    public ValueTask<FormatInspection> InspectAsync(Stream content, CancellationToken cancellationToken)
        => ValueTask.FromResult(FormatInspection.Supported(MediaType.Image));
}
