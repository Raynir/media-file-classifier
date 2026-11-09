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
    /// Gets whether a stage key, e.g. a <c>MediaItemError</c>'s stage, belongs to the analysis pipeline.
    /// </summary>
    /// <param name="stage">The stage key.</param>
    /// <returns><c>true</c> for <see cref="Item"/> and <see cref="EmbeddingError"/>.</returns>
    public static bool IsAnalysisStage(string stage) => stage is Item or EmbeddingError;

    /// <summary>
    /// Gets an English name for each stage, for progress until the UI localizes them.
    /// </summary>
    public static IReadOnlyDictionary<string, string> DisplayNames { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Item] = "Analyzing",
    };
}
