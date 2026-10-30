namespace MediaFileClassifier.Prototypes.Playback.Engines;

/// <summary>
/// One decoded frame in BGRA, valid only during the <see cref="IFrameSink.OnFrame"/> call.
/// </summary>
internal readonly ref struct VideoFrame(ReadOnlySpan<byte> pixels, int width, int height, int stride)
{
    public ReadOnlySpan<byte> Pixels { get; } = pixels;

    public int Width { get; } = width;

    public int Height { get; } = height;

    public int Stride { get; } = stride;
}

/// <summary>
/// Receives frames on the decoder thread: the Avalonia surface in the window, or the probe in <c>measure</c>.
/// </summary>
internal interface IFrameSink
{
    void OnFrame(VideoFrame frame);
}
