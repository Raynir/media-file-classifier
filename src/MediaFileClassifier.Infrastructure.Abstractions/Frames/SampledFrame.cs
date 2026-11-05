using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Abstractions.Frames;

/// <summary>
/// A frame sampled from a video or animated image, upright and as displayed, ready for the analyzers.
/// </summary>
/// <remarks>
/// <see cref="Pixels"/> is RGBA, 8 bits per channel, row-major with no padding (<c>Width * 4</c> bytes per row).
/// Transparent areas are composited over white, as the perceptual hash sees them, so alpha is always 255.
/// </remarks>
public sealed record SampledFrame
{
    /// <summary>
    /// Gets the position of the frame among the kept frames, from 0.
    /// </summary>
    public required int Index { get; init; }

    /// <summary>
    /// Gets the frame's position in the media. For animated images, the sum of the preceding frame delays.
    /// </summary>
    public required TimeSpan Timestamp { get; init; }

    /// <summary>
    /// Gets the frame's number in an animated image, from 0, or <c>null</c> for a video.
    /// </summary>
    public int? FrameNumber { get; init; }

    /// <summary>
    /// Gets the 64-bit perceptual hash of the frame.
    /// </summary>
    public required ulong PerceptualHash { get; init; }

    /// <summary>
    /// Gets the width in pixels.
    /// </summary>
    public required int Width { get; init; }

    /// <summary>
    /// Gets the height in pixels.
    /// </summary>
    public required int Height { get; init; }

    /// <summary>
    /// Gets the pixels, RGBA, row-major.
    /// </summary>
    public required ReadOnlyMemory<byte> Pixels { get; init; }

    /// <summary>
    /// Makes the keyframe to store for this frame, with a new ID.
    /// </summary>
    /// <param name="itemId">The item the frame was sampled from.</param>
    /// <returns>The keyframe.</returns>
    public Keyframe ToKeyframe(Id<MediaItem> itemId) =>
        new(Id<Keyframe>.New(), itemId, Index, Timestamp, FrameNumber, PerceptualHash);
}
