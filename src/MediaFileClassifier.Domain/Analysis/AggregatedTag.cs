using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.Domain.Analysis;

/// <summary>
/// An item-level tag combined from keyframe tags (<see cref="KeyframeAggregation.MergeTags"/>).
/// </summary>
/// <param name="Name">The tag.</param>
/// <param name="Confidence">The sum of the per-frame confidences divided by the number of keyframes, 0 to 1.</param>
/// <param name="FrameCount">How many keyframes the tag appears in.</param>
public sealed record AggregatedTag(TagName Name, double Confidence, int FrameCount);
