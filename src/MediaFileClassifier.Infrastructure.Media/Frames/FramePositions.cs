using MediaFileClassifier.Infrastructure.Abstractions.Frames;

namespace MediaFileClassifier.Infrastructure.Media.Frames;

/// <summary>
/// Where frames are sampled (E06-S02-T02, T03): one position per <see cref="FrameSamplingOptions.Interval"/> of the
/// duration, between the minimum and maximum count, at the midpoints of equal slices, so a fade at the very start or
/// end isn't hit.
/// </summary>
internal static class FramePositions
{
    /// <summary>
    /// Gets how many positions a duration gets.
    /// </summary>
    /// <param name="duration">The duration.</param>
    /// <param name="options">The options.</param>
    /// <returns>The count.</returns>
    public static int Count(TimeSpan duration, FrameSamplingOptions options)
    {
        var slices = duration > TimeSpan.Zero ? Math.Floor(duration / options.Interval) : 0;
        return (int)Math.Clamp(slices, options.MinFrames, options.MaxFrames);
    }

    /// <summary>
    /// Gets the positions to read in a video. Only the start when the duration is unknown.
    /// </summary>
    /// <param name="duration">The video's duration.</param>
    /// <param name="options">The options.</param>
    /// <returns>The positions, in order.</returns>
    public static IReadOnlyList<TimeSpan> ForVideo(TimeSpan? duration, FrameSamplingOptions options)
    {
        if (duration is not { } length || length <= TimeSpan.Zero)
        {
            return [TimeSpan.Zero];
        }

        var count = Count(length, options);
        return [.. Enumerable.Range(0, count).Select(slice => Midpoint(length, slice, count))];
    }

    /// <summary>
    /// Gets the frames to read in an animation, by display time: the frame shown at each position. An animation with
    /// no more frames than positions uses every frame. When the delays add up to nothing, every frame counts as
    /// equally long.
    /// </summary>
    /// <param name="delays">Each frame's delay, as stored.</param>
    /// <param name="options">The options.</param>
    /// <returns>The frame numbers, ascending and distinct.</returns>
    public static IReadOnlyList<int> ForAnimation(IReadOnlyList<TimeSpan> delays, FrameSamplingOptions options)
    {
        ArgumentNullException.ThrowIfNull(delays);
        if (delays.Count == 0)
        {
            return [0];
        }

        var total = delays.Aggregate(TimeSpan.Zero, (sum, delay) => sum + Positive(delay));
        var count = Count(total, options);
        if (delays.Count <= count)
        {
            return [.. Enumerable.Range(0, delays.Count)];
        }

        if (total <= TimeSpan.Zero)
        {
            return [.. Enumerable.Range(0, count).Select(slice => (int)((slice + 0.5) * delays.Count / count)).Distinct()];
        }

        var picks = new List<int>(count);
        var frame = 0;
        var frameEnd = Positive(delays[0]);
        for (var slice = 0; slice < count; slice++)
        {
            var position = Midpoint(total, slice, count);
            while (position >= frameEnd && frame < delays.Count - 1)
            {
                frame++;
                frameEnd += Positive(delays[frame]);
            }

            if (picks.Count == 0 || picks[^1] != frame)
            {
                picks.Add(frame);
            }
        }

        return picks;
    }

    /// <summary>
    /// Gets when a frame of an animation starts: the sum of the delays before it.
    /// </summary>
    /// <param name="delays">Each frame's delay, as stored.</param>
    /// <param name="frame">The frame number.</param>
    /// <returns>The start time.</returns>
    public static TimeSpan StartOf(IReadOnlyList<TimeSpan> delays, int frame) =>
        delays.Take(frame).Aggregate(TimeSpan.Zero, (sum, delay) => sum + Positive(delay));

    private static TimeSpan Midpoint(TimeSpan duration, int slice, int count) => duration * ((slice + 0.5) / count);

    private static TimeSpan Positive(TimeSpan delay) => delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
}
