namespace MediaFileClassifier.Domain.Jobs;

/// <summary>
/// Allowed changes of <see cref="ProcessingJobState"/>.
/// </summary>
/// <remarks>
/// <code>
/// Queued          → Running | Cancelled | WaitingForModel | Paused
/// Running         → Completed | Failed | Cancelled | WaitingForModel | Queued | Paused   (Queued: new work after it finished)
/// WaitingForModel → Queued | Cancelled | Paused                                (Queued: the model was installed)
/// Completed       → Queued                                                     (more work added, e.g. a retry)
/// Failed          → Queued                                                     (retry, E05-S04)
/// Cancelled       → Queued                                                     (retry)
/// Paused          → Queued | Cancelled | Completed | Failed                    (E05-S05; finished: its last running work ended while it was paused)
/// </code>
/// </remarks>
public static class ProcessingJobStateTransitions
{
    private static readonly Dictionary<ProcessingJobState, ProcessingJobState[]> Allowed = new()
    {
        [ProcessingJobState.Queued] = [ProcessingJobState.Running, ProcessingJobState.Cancelled, ProcessingJobState.WaitingForModel, ProcessingJobState.Paused],
        [ProcessingJobState.Running] =
        [
            ProcessingJobState.Completed,
            ProcessingJobState.Failed,
            ProcessingJobState.Cancelled,
            ProcessingJobState.WaitingForModel,
            ProcessingJobState.Queued,
            ProcessingJobState.Paused,
        ],
        [ProcessingJobState.WaitingForModel] = [ProcessingJobState.Queued, ProcessingJobState.Cancelled, ProcessingJobState.Paused],
        [ProcessingJobState.Completed] = [ProcessingJobState.Queued],
        [ProcessingJobState.Failed] = [ProcessingJobState.Queued],
        [ProcessingJobState.Cancelled] = [ProcessingJobState.Queued],
        [ProcessingJobState.Paused] = [ProcessingJobState.Queued, ProcessingJobState.Cancelled, ProcessingJobState.Completed, ProcessingJobState.Failed],
    };

    public static bool IsAllowed(ProcessingJobState from, ProcessingJobState to) =>
        from == to || (Allowed.TryGetValue(from, out var targets) && targets.Contains(to));

    /// <summary>
    /// Gets whether a job in this state is finished: completed, failed or cancelled.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <returns>Whether the job is finished.</returns>
    public static bool IsFinished(ProcessingJobState state) =>
        state is ProcessingJobState.Completed or ProcessingJobState.Failed or ProcessingJobState.Cancelled;
}
