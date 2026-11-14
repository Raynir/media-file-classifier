using MediaFileClassifier.Infrastructure.Abstractions.Hashing;

namespace MediaFileClassifier.Infrastructure.Abstractions.Frames;

/// <summary>
/// How frames are sampled from a video or animated image (E06-S02). The defaults are the MVP's; the analysis profile
/// will map onto these values when it exists.
/// </summary>
public sealed record FrameSamplingOptions
{
    /// <summary>
    /// Gets the default options.
    /// </summary>
    public static FrameSamplingOptions Default { get; } = new();

    /// <summary>
    /// Gets the fewest positions sampled. An animation with fewer frames uses all of them.
    /// </summary>
    public int MinFrames { get; init; } = 4;

    /// <summary>
    /// Gets the most positions sampled.
    /// </summary>
    public int MaxFrames { get; init; } = 16;

    /// <summary>
    /// Gets the duration each sampled position stands for: the count is the duration divided by this, rounded down,
    /// between <see cref="MinFrames"/> and <see cref="MaxFrames"/>.
    /// </summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets the largest perceptual-hash distance to the previous kept frame at which a frame is dropped as a near
    /// duplicate. Defaults to <see cref="PerceptualHash.SamePictureThreshold"/>.
    /// </summary>
    public int DuplicateThreshold { get; init; } = PerceptualHash.SamePictureThreshold;

    /// <summary>
    /// Gets the longest edge of the frames handed out. Larger frames are scaled down, smaller ones never up.
    /// </summary>
    public int MaxLongestEdge { get; init; } = 1024;

    /// <summary>
    /// Gets a value indicating whether blank frames (nearly black or one flat colour) are dropped. If every frame is
    /// blank, the brightest is kept.
    /// </summary>
    public bool DropBlankFrames { get; init; } = true;

    /// <summary>
    /// Gets how many positions a duration gets: one per <see cref="Interval"/>, rounded down, between
    /// <see cref="MinFrames"/> and <see cref="MaxFrames"/>.
    /// </summary>
    /// <param name="duration">The duration.</param>
    /// <returns>The count.</returns>
    public int FrameCount(TimeSpan duration)
    {
        var slices = duration > TimeSpan.Zero ? Math.Floor(duration / Interval) : 0;
        return (int)Math.Clamp(slices, MinFrames, MaxFrames);
    }

    /// <summary>
    /// Checks the options.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MinFrames, 1, nameof(MinFrames));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxFrames, MinFrames, nameof(MaxFrames));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Interval, TimeSpan.Zero, nameof(Interval));
        ArgumentOutOfRangeException.ThrowIfNegative(DuplicateThreshold, nameof(DuplicateThreshold));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(DuplicateThreshold, 64, nameof(DuplicateThreshold));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxLongestEdge, 16, nameof(MaxLongestEdge));
    }
}
