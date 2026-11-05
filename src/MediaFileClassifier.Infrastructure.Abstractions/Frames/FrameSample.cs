using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.Infrastructure.Abstractions.Frames;

/// <summary>
/// The frames sampled from one item, or none and the reason as a warning.
/// </summary>
public sealed record FrameSample
{
    /// <summary>
    /// Gets the kept frames in time order, indexed from 0. Empty when none could be read.
    /// </summary>
    public IReadOnlyList<SampledFrame> Frames { get; init; } = [];

    /// <summary>
    /// Gets the problems met, such as <see cref="FrameSamplingWarnings.FrameSamplingFailed"/>.
    /// </summary>
    public IReadOnlyList<ExtractionWarning> Warnings { get; init; } = [];

    /// <summary>
    /// Gets how many frames were read before blank frames and near duplicates were dropped.
    /// </summary>
    public int FramesRead { get; init; }

    /// <summary>
    /// Creates a result without frames.
    /// </summary>
    /// <param name="warning">Why there are none.</param>
    /// <returns>The result.</returns>
    public static FrameSample Failed(ExtractionWarning warning) => new() { Warnings = [warning] };
}
