using MediaFileClassifier.Domain.Identity;

namespace MediaFileClassifier.Domain.Analysis;

/// <summary>
/// Snapshot of the analysis run a value came from. It's copied into the value, so it stays meaningful
/// after the run itself is pruned (D020).
/// </summary>
/// <param name="RunId">The run. It may no longer exist.</param>
/// <param name="Analyzer">Analyzer key of the run.</param>
/// <param name="ModelId">Model the run used.</param>
/// <param name="PromptVersion">Prompt version the run used, if any.</param>
/// <param name="RunAt">When the run started.</param>
public sealed record RunReference(
    Id<AnalysisRun> RunId,
    string Analyzer,
    Id<ModelRegistration> ModelId,
    string? PromptVersion,
    DateTimeOffset RunAt);
