using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;

namespace MediaFileClassifier.Infrastructure.Abstractions.Formats;

/// <summary>
/// Describes one supported file format (E03-S01, spec 1.11): its extensions, its content signature and the kind of
/// media it holds. Adding a format means registering another handler; the import pipeline doesn't change.
/// </summary>
/// <remarks>
/// A handler also owns the components that process its format (D098): its <see cref="MetadataExtractor"/> and
/// <see cref="ThumbnailGenerator"/>. The analysis frame provider is added as a property by its Story.
/// </remarks>
public interface IMediaFormatHandler
{
    /// <summary>
    /// Gets the format id, such as <c>jpeg</c> or <c>mp4</c>. It is stored as the item's container format.
    /// </summary>
    string FormatId { get; }

    /// <summary>
    /// Gets the MIME type, such as <c>image/jpeg</c>.
    /// </summary>
    string MimeType { get; }

    /// <summary>
    /// Gets the file extensions, lower-case with the leading dot, such as <c>.jpg</c>. Only files with a registered
    /// extension are read; the extension is a first filter, and the content decides the format.
    /// </summary>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>
    /// Gets the component that reads the format's technical metadata (E03-S03).
    /// </summary>
    IMediaMetadataExtractor MetadataExtractor { get; }

    /// <summary>
    /// Gets the component that makes the format's thumbnails (E03-S04).
    /// </summary>
    IThumbnailGenerator ThumbnailGenerator { get; }

    /// <summary>
    /// Gets the number of leading bytes <see cref="MatchesSignature"/> needs.
    /// </summary>
    int SignatureLength { get; }

    /// <summary>
    /// Checks the file's leading bytes against the format's signature (magic bytes).
    /// </summary>
    /// <param name="header">The first <see cref="SignatureLength"/> bytes, or fewer when the file is shorter.</param>
    /// <returns><see langword="true"/> when the content is this format.</returns>
    bool MatchesSignature(ReadOnlySpan<byte> header);

    /// <summary>
    /// Looks inside a file whose signature matched, to tell the kind of media, such as a static or an animated GIF,
    /// or a video or an audio-only MP4. Reads only as much as it needs.
    /// </summary>
    /// <param name="content">The file content, positioned at the start. It may not be seekable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The kind of media, or why the file isn't supported.</returns>
    /// <exception cref="IOException">The file couldn't be read.</exception>
    ValueTask<FormatInspection> InspectAsync(Stream content, CancellationToken cancellationToken);
}
