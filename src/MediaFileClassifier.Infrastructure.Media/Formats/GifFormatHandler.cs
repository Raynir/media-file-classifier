using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;

namespace MediaFileClassifier.Infrastructure.Media.Formats;

/// <summary>
/// GIF images: a still image with one frame, an animated image with more.
/// </summary>
public sealed class GifFormatHandler : IMediaFormatHandler
{
    /// <inheritdoc />
    public string FormatId => "gif";

    /// <inheritdoc />
    public string MimeType => "image/gif";

    /// <inheritdoc />
    public IReadOnlyList<string> Extensions { get; } = [".gif"];

    /// <inheritdoc />
    public int SignatureLength => 6;

    /// <inheritdoc />
    public bool MatchesSignature(ReadOnlySpan<byte> header) => header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8);

    /// <inheritdoc />
    public ValueTask<FormatInspection> InspectAsync(Stream content, CancellationToken cancellationToken)
        => ValueTask.FromResult(FormatInspection.Supported(MediaType.Image));
}
