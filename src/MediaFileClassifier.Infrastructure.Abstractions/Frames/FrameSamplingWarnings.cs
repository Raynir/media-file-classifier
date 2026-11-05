namespace MediaFileClassifier.Infrastructure.Abstractions.Frames;

/// <summary>
/// The <see cref="Domain.Metadata.ExtractionWarning"/> codes of frame sampling.
/// </summary>
public static class FrameSamplingWarnings
{
    /// <summary>
    /// No frame could be read, e.g. because the file can't be decoded. Detail: the error.
    /// </summary>
    public const string FrameSamplingFailed = nameof(FrameSamplingFailed);

    /// <summary>
    /// FFmpeg isn't available, so a video has no frames (D068). Sampling again after FFmpeg is installed reads them.
    /// </summary>
    public const string FrameSamplingFfmpegUnavailable = nameof(FrameSamplingFfmpegUnavailable);
}
