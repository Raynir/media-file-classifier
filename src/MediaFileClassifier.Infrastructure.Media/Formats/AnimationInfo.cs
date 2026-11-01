namespace MediaFileClassifier.Infrastructure.Media.Formats;

/// <summary>
/// The frame structure of a GIF or WebP file.
/// </summary>
/// <param name="FrameCount">The number of frames found.</param>
/// <param name="Duration">The sum of the frame delays, as stored. Browsers play very short GIF delays slower, so
/// this can differ from what a viewer shows.</param>
/// <param name="LoopCount">How many times the animation repeats: <c>0</c> means forever, <c>null</c> that the
/// file doesn't say.</param>
internal sealed record AnimationInfo(int FrameCount, TimeSpan Duration, int? LoopCount)
{
    public static AnimationInfo Empty { get; } = new(0, TimeSpan.Zero, null);
}
