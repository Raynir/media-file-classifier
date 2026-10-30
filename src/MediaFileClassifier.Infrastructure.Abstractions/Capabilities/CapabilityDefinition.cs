namespace MediaFileClassifier.Infrastructure.Abstractions.Capabilities;

/// <summary>
/// A feature that may be unavailable on some machines or platforms, e.g. video analysis without FFmpeg.
/// </summary>
/// <param name="Id">Stable identifier, e.g. <c>media.video-analysis</c>.</param>
/// <param name="Name">
/// PascalCase name, e.g. <c>VideoAnalysis</c>. The UI shows the resource string <c>Capability_&lt;Name&gt;</c>,
/// so this layer holds no user-facing text (E13-S08).
/// </param>
public sealed record CapabilityDefinition(string Id, string Name);
