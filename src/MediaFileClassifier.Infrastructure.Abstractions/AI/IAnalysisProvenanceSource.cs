using MediaFileClassifier.Domain.Analysis;

namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// Gives what the next analysis of the open library would use, per enabled analyzer (E06-S07-T01), so results from
/// anything else count as outdated, e.g. in the "Outdated analysis" filter.
/// </summary>
public interface IAnalysisProvenanceSource
{
    /// <summary>
    /// Gets the current provenance of each enabled analyzer that has a usable provider.
    /// </summary>
    /// <returns>The provenances; empty when no library is open.</returns>
    IReadOnlyList<AnalyzerProvenance> GetCurrent();
}
