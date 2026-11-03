namespace MediaFileClassifier.Infrastructure.Abstractions.Jobs;

/// <summary>
/// The counters of one stage of a job (E05-S02-T01), counted in work items. Cancelled work items aren't counted.
/// </summary>
/// <param name="Stage">Stage key, e.g. <c>import.fingerprint</c>.</param>
/// <param name="Total">Work items of the stage: queued, running, completed or failed.</param>
/// <param name="Done">Completed work items.</param>
/// <param name="Failed">Failed work items.</param>
/// <param name="IsWholeTarget">Whether the stage runs for the job's whole target rather than per media item, e.g. discovery.</param>
public sealed record StageProgress(string Stage, int Total, int Done, int Failed, bool IsWholeTarget)
{
    /// <summary>
    /// Gets the work items still queued or running.
    /// </summary>
    public int Pending => Total - Done - Failed;
}
