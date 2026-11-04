using MediaFileClassifier.Infrastructure.Abstractions.Metadata;

namespace MediaFileClassifier.Infrastructure.Abstractions.Previews;

/// <summary>
/// Decodes original images for the details panel preview (E14-S05-T02): at about the screen's size first, which is
/// quick even for very large JPEGs, and at full resolution for zooming in.
/// </summary>
public interface IPreviewDecoder
{
    /// <summary>
    /// Decodes a still image, or the first frame of an animated one, as stored (orientation not applied).
    /// </summary>
    /// <param name="content">The file.</param>
    /// <param name="maxLongestEdge">The decoded image's longer side at most, or <see langword="null"/> for full resolution.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The pixels, which the caller disposes.</returns>
    /// <exception cref="InvalidDataException">The file can't be decoded.</exception>
    /// <exception cref="IOException">The file can't be read.</exception>
    Task<DecodedImage> DecodeAsync(MediaContent content, int? maxLongestEdge, CancellationToken cancellationToken);
}

/// <summary>
/// Decoded pixels: 32-bit BGRA, premultiplied alpha, sRGB.
/// </summary>
public sealed class DecodedImage : IDisposable
{
    private IDisposable? owner;

    /// <summary>
    /// Initializes a new instance of the <see cref="DecodedImage"/> class.
    /// </summary>
    /// <param name="width">The decoded width.</param>
    /// <param name="height">The decoded height.</param>
    /// <param name="rowBytes">The bytes per row.</param>
    /// <param name="pixels">The first pixel, valid until the image is disposed.</param>
    /// <param name="isFullResolution">Whether the image wasn't scaled down.</param>
    /// <param name="owner">Owns the pixels.</param>
    public DecodedImage(int width, int height, int rowBytes, nint pixels, bool isFullResolution, IDisposable owner)
    {
        Width = width;
        Height = height;
        RowBytes = rowBytes;
        Pixels = pixels;
        IsFullResolution = isFullResolution;
        this.owner = owner;
    }

    /// <summary>Gets the decoded width.</summary>
    public int Width { get; }

    /// <summary>Gets the decoded height.</summary>
    public int Height { get; }

    /// <summary>Gets the bytes per row.</summary>
    public int RowBytes { get; }

    /// <summary>Gets the first pixel, valid until the image is disposed.</summary>
    public nint Pixels { get; }

    /// <summary>Gets a value indicating whether the image wasn't scaled down.</summary>
    public bool IsFullResolution { get; }

    /// <inheritdoc />
    public void Dispose() => Interlocked.Exchange(ref owner, null)?.Dispose();
}
