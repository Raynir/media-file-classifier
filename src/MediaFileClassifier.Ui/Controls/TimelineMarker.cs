namespace MediaFileClassifier.Ui.Controls;

/// <summary>
/// What a <see cref="TimelineMarker"/> marks.
/// </summary>
public enum TimelineMarkerKind
{
    /// <summary>A keyframe sampled during analysis (E06-S02, E14-S06-T02).</summary>
    Keyframe,

    /// <summary>A moment that matched the search (E09-S06, later).</summary>
    Match,
}

/// <summary>
/// A moment marked on the <see cref="VideoTimeline"/>; clicking it seeks there.
/// </summary>
/// <param name="Time">Where it is in the video.</param>
/// <param name="Kind">What it marks.</param>
public sealed record TimelineMarker(TimeSpan Time, TimelineMarkerKind Kind = TimelineMarkerKind.Keyframe);
