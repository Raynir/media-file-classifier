using MediaFileClassifier.Infrastructure.Abstractions.Frames;

namespace MediaFileClassifier.Infrastructure.Media.Frames;

/// <summary>
/// Where frames are sampled (E06-S02-T02): one position per <see cref="FrameSamplingOptions.Interval"/> of the
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

    private static TimeSpan Midpoint(TimeSpan duration, int slice, int count) => duration * ((slice + 0.5) / count);
}
