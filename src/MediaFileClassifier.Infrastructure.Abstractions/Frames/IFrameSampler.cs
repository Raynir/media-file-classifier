using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;

namespace MediaFileClassifier.Infrastructure.Abstractions.Frames;

/// <summary>
/// Samples the keyframes of a video or animated image for analysis (E06-S02, D013).
/// </summary>
/// <remarks>
/// Positions are the midpoints of equal slices of the duration; animated images are sampled by display time, not by
/// frame index. Blank frames and frames within <see cref="FrameSamplingOptions.DuplicateThreshold"/> of the previous
/// kept frame are dropped. Video frames come from FFmpeg, one seek per position; GIF and WebP frames are decoded
/// directly. CPU- and process-bound, so call it from a background worker.
/// </remarks>
public interface IFrameSampler
{
    /// <summary>
    /// Samples an item's frames.
    /// </summary>
    /// <param name="content">The file.</param>
    /// <param name="format">The detected format; its media type must be a video or an animated image.</param>
    /// <param name="metadata">The item's technical metadata, for the duration and orientation.</param>
    /// <param name="options">How to sample.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The frames, or none and a warning. Doesn't throw for an unreadable file.</returns>
    /// <exception cref="ArgumentException">The media type is a still image.</exception>
    Task<FrameSample> SampleAsync(
        MediaContent content,
        MediaFormatDetection format,
        TechnicalMetadata metadata,
        FrameSamplingOptions options,
        CancellationToken cancellationToken);
}
