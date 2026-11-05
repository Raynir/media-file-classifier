using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.Domain.Analysis;

/// <summary>
/// A tag an analyzer found in one keyframe.
/// </summary>
/// <remarks>Invariants: the confidence is between 0 and 1.</remarks>
public sealed record FrameTag
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FrameTag"/> class.
    /// </summary>
    /// <param name="name">The tag.</param>
    /// <param name="confidence">The analyzer's confidence, 0 to 1.</param>
    public FrameTag(TagName name, double confidence)
    {
        ArgumentNullException.ThrowIfNull(name);
        Confidences.EnsureValid(confidence);
        Name = name;
        Confidence = confidence;
    }

    public TagName Name { get; }

    public double Confidence { get; }
}
