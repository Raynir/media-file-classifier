namespace MediaFileClassifier.Domain.Jobs;

/// <summary>
/// State of a work item. Transitions are listed in <see cref="WorkItemStateTransitions"/>.
/// </summary>
public enum WorkItemState
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled,
}
