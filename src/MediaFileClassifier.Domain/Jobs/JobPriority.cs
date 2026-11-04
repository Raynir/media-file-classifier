namespace MediaFileClassifier.Domain.Jobs;

/// <summary>
/// Priority levels of processing jobs and their work items (E05-S01), stored as <see cref="ProcessingJob.Priority"/>.
/// A higher value runs first.
/// </summary>
public enum JobPriority
{
    /// <summary>
    /// Maintenance, such as optimizing the database.
    /// </summary>
    Maintenance = 0,

    /// <summary>
    /// Full content hashing (E01-S06-T02).
    /// </summary>
    FullHashing = 10,

    /// <summary>
    /// AI analysis.
    /// </summary>
    AiAnalysis = 20,

    /// <summary>
    /// Import stages: fingerprint, metadata, thumbnail, register.
    /// </summary>
    Import = 30,

    /// <summary>
    /// Actions the user asked for, e.g. "Re-analyze this item".
    /// </summary>
    UserAction = 40,
}
