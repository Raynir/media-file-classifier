using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;

namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// An effective tag of an item, with the provenance of the assignment that won.
/// </summary>
/// <param name="Tag">The tag.</param>
/// <param name="Source">Layer the winning assignment comes from.</param>
/// <param name="Origin">How the user created it, for user tags.</param>
/// <param name="RunId">Run that produced an AI tag, or that a user tag was accepted from.</param>
/// <param name="ModelId">Model that produced an AI tag, or that a user tag was accepted from.</param>
/// <param name="Confidence">AI confidence, if any.</param>
public sealed record ResolvedTag(
    TagName Tag,
    MetadataSource Source,
    UserValueOrigin? Origin,
    Id<AnalysisRun>? RunId,
    Id<ModelRegistration>? ModelId,
    double? Confidence);
