using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;

namespace MediaFileClassifier.Infrastructure.Media.Formats;

/// <summary>
/// MP4 video.
/// </summary>
public sealed class Mp4FormatHandler : IMediaFormatHandler
{
    /// <inheritdoc />
    public string FormatId => "mp4";

    /// <inheritdoc />
    public string MimeType => "video/mp4";

    /// <inheritdoc />
    public IReadOnlyList<string> Extensions { get; } = [".mp4", ".m4v"];

    /// <inheritdoc />
    public int SignatureLength => 8;

    /// <inheritdoc />
    public bool MatchesSignature(ReadOnlySpan<byte> header) => header.Length >= 8 && header[4..8].SequenceEqual("ftyp"u8);

    /// <inheritdoc />
    public ValueTask<FormatInspection> InspectAsync(Stream content, CancellationToken cancellationToken)
        => ValueTask.FromResult(FormatInspection.Supported(MediaType.Video));
}
