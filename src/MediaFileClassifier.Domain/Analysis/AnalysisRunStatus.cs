namespace MediaFileClassifier.Domain.Analysis;

/// <summary>
/// Execution status of an analysis run.
/// </summary>
public enum AnalysisRunStatus
{
    /// <summary>
    /// The analyzer is working on the item.
    /// </summary>
    Running,

    /// <summary>
    /// The analyzer finished and its results are stored with the run.
    /// </summary>
    Completed,

    /// <summary>
    /// The analyzer failed. A failed run never becomes active.
    /// </summary>
    Failed,
}
