using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediatR;

namespace MediaFileClassifier.UseCases.Processing.GetProcessingQueue;

/// <summary>
/// Gets the jobs for the processing panel (E05-S03-T01): the unfinished ones, and the ones that finished last.
/// </summary>
/// <param name="RecentLimit">How many finished jobs to return.</param>
public sealed record GetProcessingQueueQuery(int RecentLimit = 20) : IRequest<ProcessingQueue>;

/// <summary>
/// The jobs of the open library, as the processing panel lists them.
/// </summary>
/// <param name="Unfinished">Queued, running and waiting jobs, oldest first.</param>
/// <param name="Recent">Finished jobs, the most recently finished first.</param>
public sealed record ProcessingQueue(IReadOnlyList<QueueJob> Unfinished, IReadOnlyList<QueueJob> Recent)
{
    /// <summary>
    /// Gets the queue when there are no jobs, or no library is open.
    /// </summary>
    public static ProcessingQueue Empty { get; } = new([], []);
}

/// <summary>
/// One job in the processing panel, with its work item counts. Cancelled work items aren't counted.
/// </summary>
/// <param name="Id">The job.</param>
/// <param name="Type">Job type key, e.g. <c>import</c>.</param>
/// <param name="Description">Description of the job.</param>
/// <param name="State">The job's state.</param>
/// <param name="CreatedAt">When the job was queued.</param>
/// <param name="StartedAt">When the job started, if it did.</param>
/// <param name="FinishedAt">When the job finished, if it did.</param>
/// <param name="Total">Work items queued, running, completed or failed.</param>
/// <param name="Done">Completed work items.</param>
/// <param name="Failed">Failures that still need attention: failed work items whose item is still in error.</param>
/// <param name="Pending">Work items still queued or running.</param>
public sealed record QueueJob(
    Id<ProcessingJob> Id,
    string Type,
    string Description,
    ProcessingJobState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int Total,
    int Done,
    int Failed,
    int Pending)
{
    /// <summary>
    /// Gets a value indicating whether the job is finished.
    /// </summary>
    public bool IsFinished => ProcessingJobStateTransitions.IsFinished(State);
}
