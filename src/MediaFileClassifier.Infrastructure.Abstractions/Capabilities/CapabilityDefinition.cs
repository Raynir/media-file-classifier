namespace MediaFileClassifier.Infrastructure.Abstractions.Capabilities;

/// <summary>
/// A feature that may be unavailable on some machines or platforms, e.g. video analysis without FFmpeg.
/// </summary>
/// <param name="Id">Stable identifier, e.g. <c>media.video-analysis</c>.</param>
/// <param name="DisplayName">Feature name for messages, e.g. "Video analysis".</param>
public sealed record CapabilityDefinition(string Id, string DisplayName);
