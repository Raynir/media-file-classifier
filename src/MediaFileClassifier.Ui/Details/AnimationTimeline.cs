namespace MediaFileClassifier.Ui.Details;

/// <summary>
/// Which frame of an animated image is showing at a given time (E14-S12-T01): the frame delays in order, looping.
/// </summary>
public sealed class AnimationTimeline
{
    /// <summary>
    /// The shortest time a frame shows, in milliseconds. A delay of zero would make the loop endless.
    /// </summary>
    public const int MinDelay = 1;

    private readonly long[] starts;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimationTimeline"/> class.
    /// </summary>
    /// <param name="delays">How long each frame shows, in milliseconds.</param>
    public AnimationTimeline(IReadOnlyList<int> delays)
    {
        ArgumentNullException.ThrowIfNull(delays);
        starts = new long[delays.Count];
        long total = 0;
        for (var i = 0; i < delays.Count; i++)
        {
            starts[i] = total;
            total += Math.Max(MinDelay, delays[i]);
        }

        Duration = TimeSpan.FromMilliseconds(total);
    }

    /// <summary>
    /// Gets the number of frames.
    /// </summary>
    public int FrameCount => starts.Length;

    /// <summary>
    /// Gets the length of one loop.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Gets the frame showing after the animation has run for a time, looping when it is longer than one loop.
    /// </summary>
    /// <param name="elapsed">The time since the first frame appeared.</param>
    /// <returns>The frame's index, or 0 for an animation without frames.</returns>
    public int IndexAt(TimeSpan elapsed)
    {
        if (starts.Length <= 1)
        {
            return 0;
        }

        var position = (long)elapsed.TotalMilliseconds % (long)Duration.TotalMilliseconds;
        var index = Array.BinarySearch(starts, position);
        return index >= 0 ? index : ~index - 1;
    }

    /// <summary>
    /// Gets the time at which a frame appears in a loop.
    /// </summary>
    /// <param name="index">The frame's index.</param>
    /// <returns>The time since the loop started.</returns>
    public TimeSpan StartOf(int index) => TimeSpan.FromMilliseconds(starts[index]);
}
