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
    Task AddJobAsync(ProcessingJob job, IReadOnlyCollection<WorkItem> workItems, CancellationToken cancellationToken = default) =>
        AddJobAsync(job, workItems, null, cancellationToken);

    /// <summary>
    /// Adds a job with its first work items, and tells which batches are stored (MFC-1010).
    /// </summary>
    /// <param name="job">The new job.</param>
    /// <param name="workItems">Its work items.</param>
    /// <param name="stored">Called with each batch once it's stored, or <see langword="null"/>. A write that fails
    /// after the first batch leaves the earlier batches stored, and they may run.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the job and its items are stored.</returns>
    Task AddJobAsync(ProcessingJob job, IReadOnlyCollection<WorkItem> workItems, Action<IReadOnlyCollection<WorkItem>>? stored, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds work items to an existing job, e.g. the next stage for an item. A finished job goes back to
    /// <see cref="ProcessingJobState.Queued"/>; a cancelled one doesn't take new work.
    /// </summary>
    /// <param name="jobId">The job.</param>
    /// <param name="workItems">The new work items.</param>
    /// <param name="now">Current time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="false"/> if the job doesn't exist or is cancelled; the batches from then on aren't
    /// added.</returns>
    Task<bool> AddWorkItemsAsync(Id<ProcessingJob> jobId, IReadOnlyCollection<WorkItem> workItems, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        AddWorkItemsAsync(jobId, workItems, now, null, cancellationToken);

    /// <summary>
    /// Adds work items to an existing job, and tells which batches are stored (MFC-1010). A finished job goes back to
    /// <see cref="ProcessingJobState.Queued"/>; a cancelled one doesn't take new work.
    /// </summary>
    /// <param name="jobId">The job.</param>
    /// <param name="workItems">The new work items.</param>
    /// <param name="now">Current time.</param>
    /// <param name="stored">Called with each batch once it's stored, or <see langword="null"/>. A write that fails or
    /// stops after the first batch leaves the earlier batches stored, and they may run.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="false"/> if the job doesn't exist or is cancelled; the batches from then on aren't
    /// added.</returns>
    Task<bool> AddWorkItemsAsync(Id<ProcessingJob> jobId, IReadOnlyCollection<WorkItem> workItems, DateTimeOffset now, Action<IReadOnlyCollection<WorkItem>>? stored, CancellationToken cancellationToken = default);

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
    /// Gets the jobs that finished (completed, failed or cancelled), the most recently finished first (E05-S03-T01).
    /// </summary>
    /// <param name="limit">Maximum number of jobs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The jobs.</returns>
    Task<IReadOnlyList<ProcessingJob>> GetRecentJobsAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the work items of some jobs by job, stage and state (E05-S03-T01), e.g. for the processing panel's rows.
    /// </summary>
    /// <param name="jobIds">The jobs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The counts, in no particular order.</returns>
    Task<IReadOnlyList<WorkItemCount>> CountWorkAsync(IReadOnlyCollection<Id<ProcessingJob>> jobIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the failures of some jobs that still need attention (E05-S03-T01): failed work items whose media item is
    /// still in error, or that have no media item. A failure the user retried successfully isn't counted, though its
    /// work item stays <see cref="WorkItemState.Failed"/> as a record.
    /// </summary>
    /// <param name="jobIds">The jobs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The failure count per job; jobs without failures are left out.</returns>
    Task<IReadOnlyDictionary<Id<ProcessingJob>, int>> CountFailuresAsync(IReadOnlyCollection<Id<ProcessingJob>> jobIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the failed work items of a job that still need attention (E05-S03-T03), the same ones
    /// <see cref="CountFailuresAsync"/> counts: failed work items whose media item is still in error, or that have no
    /// media item.
    /// </summary>
    /// <param name="jobId">The job.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The failed work items, oldest first.</returns>
    Task<IReadOnlyList<WorkItem>> GetFailedWorkItemsAsync(Id<ProcessingJob> jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets queued work items of a pool in the order they should run: highest priority first, then oldest.
    /// </summary>
    /// <param name="pool">The pool.</param>
    /// <param name="limit">Maximum number of items.</param>
    /// <param name="excluding">Work items to leave out, e.g. those the caller already holds.</param>
    /// <param name="excludingStages">Stages to leave out, e.g. those waiting for a model, or <see langword="null"/>.</param>
    /// <param name="dueBy">Leaves out retries due after this time (E05-S04-T02), or <see langword="null"/> to include them.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The work items.</returns>
    Task<IReadOnlyList<WorkItem>> GetQueuedAsync(
        ResourcePool pool,
        int limit,
        IReadOnlyCollection<Id<WorkItem>> excluding,
        IReadOnlyCollection<string>? excludingStages = null,
        DateTimeOffset? dueBy = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets when the next queued retry is due (E05-S04-T02).
    /// </summary>
    /// <param name="after">Only retries due after this time count.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The earliest retry time after <paramref name="after"/>, or <see langword="null"/> if there's none. Parked work
    /// (<see cref="WorkItem.IsParked"/>) doesn't count.
    /// </returns>
    Task<DateTimeOffset?> GetNextRetryAtAsync(DateTimeOffset after, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases every parked work item (E05-S04-T03), so it may start at once.
    /// </summary>
    /// <param name="now">Current time, the released work's retry time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of work items released.</returns>
    Task<int> ReleaseParkedAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

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
    /// Counts the work items still to do (queued or running) in the whole library. Queued work of jobs that wait for a
    /// model (D037) or are paused (E05-S05) isn't counted: it can't run until the model is installed or the job resumed.
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
    /// <returns>The running work item, or <see langword="null"/> if it's no longer queued (e.g. its job was cancelled) or its job is paused.</returns>
    Task<WorkItem?> StartAsync(WorkItem workItem, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the new state of a running work item. When it was the job's last pending work item, the job finishes in
    /// the same write: <see cref="ProcessingJobState.Completed"/> if any of its items completed, even when others
    /// failed (they keep their error for a retry), and <see cref="ProcessingJobState.Failed"/> if they all failed.
    /// A cancelled job stays cancelled. A paused job finishes the same way once nothing of it is pending.
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
    /// Pauses a job (E05-S05): its queued work items stay queued but aren't started until it's resumed. Running items
    /// are the scheduler's to settle.
    /// </summary>
    /// <param name="jobId">The job.</param>
    /// <param name="now">Current time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="false"/> if the job doesn't exist, is finished, or is already paused.</returns>
    Task<bool> PauseJobAsync(Id<ProcessingJob> jobId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a paused job (E05-S05): it's queued again, so its work runs.
    /// </summary>
    /// <param name="jobId">The job.</param>
    /// <param name="now">Current time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="false"/> if the job doesn't exist or isn't paused.</returns>
    Task<bool> ResumeJobAsync(Id<ProcessingJob> jobId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes every paused job, e.g. when a library opens and paused states aren't kept after a restart (E05-S05-T03).
    /// </summary>
    /// <param name="now">Current time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of jobs resumed.</returns>
    Task<int> ResumeAllJobsAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restart recovery (E05-S01-T05): puts work items left running by a crash or a closed app back in the queue, and
    /// their jobs back to <see cref="ProcessingJobState.Queued"/>.
    /// </summary>
    /// <param name="now">Current time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of work items put back.</returns>
    Task<int> RequeueRunningAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes old finished jobs with their work items (E05-S03-T04): the finished jobs beyond the newest
    /// <paramref name="keepLast"/> by finish time, and the finished jobs that finished before
    /// <paramref name="olderThan"/>. A job that isn't finished is never deleted, and the state is checked again as the
    /// rows are deleted, so a finished job that new work woke up in the meantime stays.
    /// </summary>
    /// <param name="keepLast">How many of the most recently finished jobs to keep.</param>
    /// <param name="olderThan">Finished jobs that finished before this time are deleted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of jobs deleted.</returns>
    Task<int> PruneFinishedJobsAsync(int keepLast, DateTimeOffset olderThan, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Stores <see cref="WorkItem.Payload"/> of a work item, replacing the earlier one. A stage that runs in several
    /// steps uses it to remember its progress, which it finds again if the item runs again after an interruption (MFC-1037).
    /// </summary>
    /// <param name="workItemId">The work item.</param>
    /// <param name="payload">The payload, as JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="false"/> if the work item doesn't exist.</returns>
    Task<bool> SetWorkItemPayloadAsync(Id<WorkItem> workItemId, string payload, CancellationToken cancellationToken = default);
}
