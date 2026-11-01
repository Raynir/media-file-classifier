using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;

namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// Outcome of applying a new analysis run to an item.
/// </summary>
/// <param name="PrunedRunIds">Older runs removed by retention, with their values and tags. Storage deletes them.</param>
/// <param name="EffectiveValuesChanged">Whether any effective value or tag changed, so the item goes back to review (D021).</param>
public sealed record RunApplication(IReadOnlyList<Id<AnalysisRun>> PrunedRunIds, bool EffectiveValuesChanged);
