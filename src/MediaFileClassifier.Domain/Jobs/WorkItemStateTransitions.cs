namespace MediaFileClassifier.Domain.Jobs;

/// <summary>
/// Allowed changes of <see cref="WorkItemState"/>.
/// </summary>
/// <remarks>
/// <code>
/// Queued    → Running | Cancelled
/// Running   → Completed | Failed | Cancelled | Queued   (Queued: the library closed, or the app stopped, while it ran)
/// Failed    → Queued                                   (retry, E05-S04)
/// Cancelled → Queued                                   (retry)
/// </code>
/// Completed is final: the work is redone by a new work item.
/// </remarks>
public static class WorkItemStateTransitions
{
    private static readonly Dictionary<WorkItemState, WorkItemState[]> Allowed = new()
    {
        [WorkItemState.Queued] = [WorkItemState.Running, WorkItemState.Cancelled],
        [WorkItemState.Running] = [WorkItemState.Completed, WorkItemState.Failed, WorkItemState.Cancelled, WorkItemState.Queued],
        [WorkItemState.Failed] = [WorkItemState.Queued],
        [WorkItemState.Cancelled] = [WorkItemState.Queued],
    };

    public static bool IsAllowed(WorkItemState from, WorkItemState to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>
    /// Gets whether a work item in this state still has work to do.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <returns><see langword="true"/> for <see cref="WorkItemState.Queued"/> and <see cref="WorkItemState.Running"/>.</returns>
    public static bool IsPending(WorkItemState state) => state is WorkItemState.Queued or WorkItemState.Running;
}
