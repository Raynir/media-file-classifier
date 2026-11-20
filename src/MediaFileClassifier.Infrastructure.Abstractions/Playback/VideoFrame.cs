namespace MediaFileClassifier.Infrastructure.Abstractions.Playback;

/// <summary>
/// One decoded video frame in BGRA (32 bits per pixel, opaque). It's only valid during the
/// <see cref="IVideoFrameSink.OnFrame"/> call, so a sink copies what it keeps.
/// </summary>
/// <param name="pixels">The pixels, <paramref name="stride"/> bytes per row.</param>
/// <param name="width">Width in pixels.</param>
/// <param name="height">Height in pixels.</param>
/// <param name="stride">Bytes per row, at least <c>width × 4</c>.</param>
public readonly ref struct VideoFrame(ReadOnlySpan<byte> pixels, int width, int height, int stride)
{
    /// <summary>
    /// Gets the pixels.
    /// </summary>
    public ReadOnlySpan<byte> Pixels { get; } = pixels;

    /// <summary>
    /// Gets the width in pixels.
    /// </summary>
    public int Width { get; } = width;

    /// <summary>
    /// Gets the height in pixels.
    /// </summary>
    public int Height { get; } = height;

    /// <summary>
    /// Gets the bytes per row.
    /// </summary>
    public int Stride { get; } = stride;
}
