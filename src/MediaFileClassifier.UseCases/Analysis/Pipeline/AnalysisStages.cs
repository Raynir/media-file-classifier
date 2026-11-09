namespace MediaFileClassifier.UseCases.Analysis.Pipeline;

/// <summary>
/// The analysis pipeline's job type and stage key (E06-S01). The keys are stored with each work item, so they never change.
/// </summary>
public static class AnalysisStages
{
    /// <summary>
    /// Job type of an analysis of a set of items.
    /// </summary>
    public const string AnalysisJob = "analysis";

    /// <summary>
    /// Runs the enabled analyzers of the library's profile on one item, in order, and stores each result.
    /// </summary>
    public const string Item = "analysis.item";

    /// <summary>
    /// The stage recorded in <c>MediaItemError</c> when the item's embedding failed for good.
    /// </summary>
    public const string EmbeddingError = "analysis.embedding";

    /// <summary>
    /// Gets an English name for each stage, for progress until the UI localizes them.
    /// </summary>
    public static IReadOnlyDictionary<string, string> DisplayNames { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Item] = "Analyzing",
    };
}
