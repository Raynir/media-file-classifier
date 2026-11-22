namespace MediaFileClassifier.Domain.Jobs;

/// <summary>
/// State of a processing job (E05-S01). Transitions are defined by the job system, not here.
/// </summary>
public enum ProcessingJobState
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled,

    /// <summary>
    /// A needed AI model isn't installed (E06-S12).
    /// </summary>
    WaitingForModel,

    /// <summary>
    /// Paused by the user (E05-S05): its queued work isn't started until it's resumed.
    /// </summary>
    Paused,
}
