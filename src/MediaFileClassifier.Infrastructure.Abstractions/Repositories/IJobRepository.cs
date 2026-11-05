using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// Stores the processing jobs and work items of an open library (E05-S01-T01). The job scheduler is its main user;
/// other code enqueues work through the job API instead.
/// </summary>
public interface IJobRepository
{
    /// <summary>
    /// Adds a job with its first work items. The items are written in batches; the job is written with the first one.
    /// </summary>
    /// <param name="job">The new job.</param>
    /// <param name="workItems">Its work items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the job and its items are stored.</returns>
    Task AddJobAsync(ProcessingJob job, IReadOnlyCollection<WorkItem> workItems, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds work items to an existing job, e.g. the next stage for an item. A finished job goes back to
    /// <see cref="ProcessingJobState.Queued"/>; a cancelled one doesn't take new work.
    /// </summary>
    /// <param name="jobId">The job.</param>
    /// <param name="workItems">The new work items.</param>
    /// <param name="now">Current time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="false"/> if the job doesn't exist or is cancelled; nothing is added then.</returns>
    Task<bool> AddWorkItemsAsync(Id<ProcessingJob> jobId, IReadOnlyCollection<WorkItem> workItems, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a job.
    /// </summary>
    /// <param name="id">Job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The job, or <see langword="null"/>.</returns>
    Task<ProcessingJob?> GetJobAsync(Id<ProcessingJob> id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the jobs that aren't finished, oldest first.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The jobs.</returns>
    Task<IReadOnlyList<ProcessingJob>> GetUnfinishedJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets queued work items of a pool in the order they should run: highest priority first, then oldest.
    /// </summary>
    /// <param name="pool">The pool.</param>
    /// <param name="limit">Maximum number of items.</param>
    /// <param name="excluding">Work items to leave out, e.g. those the caller already holds.</param>
    /// <param name="excludingStages">Stages to leave out, e.g. those waiting for a model, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The work items.</returns>
    Task<IReadOnlyList<WorkItem>> GetQueuedAsync(
        ResourcePool pool,
        int limit,
        IReadOnlyCollection<Id<WorkItem>> excluding,
        IReadOnlyCollection<string>? excludingStages = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the queued work items for some media items, e.g. those on screen (E05-S01-T03).
    /// </summary>
    /// <param name="itemIds">The media items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The work items, in no particular order.</returns>
    Task<IReadOnlyList<WorkItem>> GetQueuedForItemsAsync(IReadOnlyCollection<Id<MediaItem>> itemIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a job's work items.
    /// </summary>
    /// <param name="jobId">The job.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The work items, oldest first.</returns>
    Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(Id<ProcessingJob> jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the work items still to do (queued or running) in the whole library. Work of jobs that wait for a model
    /// isn't counted: it can't run until the model is installed (D037).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of work items.</returns>
    Task<int> CountPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the work items of the unfinished jobs by job, stage and state, e.g. to seed the progress counters when
    /// a library opens (E05-S02-T01).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The counts, in no particular order.</returns>
    Task<IReadOnlyList<WorkItemCount>> CountUnfinishedWorkAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a queued work item as running, and its job too if it's queued. Both changes are one write.
    /// </summary>
    /// <param name="workItem">The work item, in <see cref="WorkItemState.Queued"/> state.</param>
    /// <param name="now">Current time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The running work item, or <see langword="null"/> if it's no longer queued, e.g. its job was cancelled.</returns>
    Task<WorkItem?> StartAsync(WorkItem workItem, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the new state of a running work item. When it was the job's last pending work item, the job finishes in
    /// the same write: <see cref="ProcessingJobState.Completed"/> if any of its items completed, even when others
    /// failed (they keep their error for a retry), and <see cref="ProcessingJobState.Failed"/> if they all failed.
    /// A cancelled job stays cancelled.
    /// </summary>
    /// <param name="workItem">The work item in its new state: completed, failed, cancelled, or queued again.</param>
    /// <param name="now">Current time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The job after the change.</returns>
    Task<ProcessingJob> FinishAsync(WorkItem workItem, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a job and its queued work items. Running items are the scheduler's to stop; finished ones keep their
    /// results.
    /// </summary>
    /// <param name="jobId">The job.</param>
    /// <param name="now">Current time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="false"/> if the job doesn't exist or was already finished.</returns>
    Task<bool> CancelJobAsync(Id<ProcessingJob> jobId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restart recovery (E05-S01-T05): puts work items left running by a crash or a closed app back in the queue, and
    /// their jobs back to <see cref="ProcessingJobState.Queued"/>.
    /// </summary>
    /// <param name="now">Current time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of work items put back.</returns>
    Task<int> RequeueRunningAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts the jobs that can't go on without a model into <see cref="ProcessingJobState.WaitingForModel"/>: queued or
    /// running jobs with nothing running, whose queued work is all in the given stages (D037).
    /// </summary>
    /// <param name="stages">The stages whose model is missing.</param>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many jobs started waiting.</returns>
    Task<int> MarkWaitingForModelAsync(IReadOnlyCollection<string> stages, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues every job that waits for a model again, after a model was installed. Jobs whose model is still missing
    /// wait again at the next dispatch.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many jobs were queued.</returns>
    Task<int> ResumeWaitingForModelAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores <see cref="ProcessingJob.Result"/> of a job, replacing an earlier one.
    /// </summary>
    /// <param name="jobId">The job.</param>
    /// <param name="result">The result, as JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="false"/> if the job doesn't exist.</returns>
    Task<bool> SetResultAsync(Id<ProcessingJob> jobId, string result, CancellationToken cancellationToken = default);
}
