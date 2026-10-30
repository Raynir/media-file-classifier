using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;

namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// The effective value of a descriptive field, with its provenance.
/// </summary>
/// <param name="Field">The field.</param>
/// <param name="Value">The effective value.</param>
/// <param name="Source">Layer the value comes from.</param>
/// <param name="Origin">How the user created it, for user values.</param>
/// <param name="RunId">Run that produced an AI value, or that a user value was accepted from.</param>
/// <param name="ModelId">Model that produced an AI value, or that a user value was accepted from.</param>
/// <param name="Confidence">AI confidence, if any.</param>
/// <param name="Timestamp">When the value was read, produced or last changed by the user.</param>
/// <param name="SuggestionRunId">Newer run with a different AI value, for user values.</param>
public sealed record ResolvedField(
    DescriptiveField Field,
    FieldValue Value,
    MetadataSource Source,
    UserValueOrigin? Origin,
    Id<AnalysisRun>? RunId,
    Id<ModelRegistration>? ModelId,
    double? Confidence,
    DateTimeOffset Timestamp,
    Id<AnalysisRun>? SuggestionRunId);
