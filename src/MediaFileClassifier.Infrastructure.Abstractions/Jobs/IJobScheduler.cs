using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;

namespace MediaFileClassifier.Infrastructure.Abstractions.Jobs;

/// <summary>
/// Runs the jobs of one open library (E05-S01): the job API for the other Epics. Get it from
/// <see cref="IJobSystem.Current"/>. Work runs in three resource pools, each with its own concurrency limit, in
/// priority order (<see cref="JobPriority"/>), never on the UI thread.
/// </summary>
public interface IJobScheduler
{
    /// <summary>
    /// Gets the library the scheduler runs jobs for.
    /// </summary>
    IOpenLibrary Library { get; }

    /// <summary>
    /// Raised after processing, a pool or a job is paused or resumed (E05-S05), on the thread that changed it.
    /// </summary>
    event EventHandler? PauseChanged;

    /// <summary>
    /// Gets whether all processing is paused and waits for <see cref="Resume"/>: by the user (E05-S05), or after the
    /// library opened with unfinished work and the settings say to ask first. Work that would start in the background,
    /// such as an import from folder monitoring, should check it too.
    /// </summary>
    bool IsPaused { get; }

    /// <summary>
    /// Enqueues a job with its first work items. They're persisted, so they survive a restart.
    /// </summary>
    /// <param name="request">The job.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new job's ID.</returns>
    /// <exception cref="ArgumentException">A work item names a stage with no registered handler.</exception>
    Task<Id<ProcessingJob>> EnqueueJobAsync(JobRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds work to a job, e.g. a handler enqueuing the next stage of its item. The work gets the job's priority.
    /// </summary>
    /// <param name="jobId">The job.</param>
    /// <param name="workItems">The work.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="false"/> if the job doesn't exist or was cancelled; nothing is added then.</returns>
    /// <exception cref="ArgumentException">A work item names a stage with no registered handler.</exception>
    Task<bool> EnqueueWorkAsync(Id<ProcessingJob> jobId, IReadOnlyList<WorkItemRequest> workItems, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a job: its queued work items are cancelled, and its running ones are stopped through their
    /// cancellation token. Work items already finished keep their results.
    /// </summary>
    /// <param name="jobId">The job.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="false"/> if the job doesn't exist or was already finished.</returns>
    Task<bool> CancelJobAsync(Id<ProcessingJob> jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the queued work for some media items ahead, e.g. thumbnails for the items on screen (E05-S01-T03). It
    /// runs after user-initiated actions and before everything else. Each call replaces the previous set, so call it
    /// again with the new set when the view scrolls, and with an empty set to clear it.
    /// </summary>
    /// <param name="itemIds">The media items.</param>
    void Boost(IReadOnlyCollection<Id<MediaItem>> itemIds);

    /// <summary>
    /// Gets the concurrency limit of a pool.
    /// </summary>
    /// <param name="pool">The pool.</param>
    /// <returns>The most work items that run at once in the pool.</returns>
    int GetPoolLimit(ResourcePool pool);

    /// <summary>
    /// Changes the concurrency limit of a pool, e.g. when the settings change. Running work isn't interrupted; a
    /// lower limit takes effect as work items finish.
    /// </summary>
    /// <param name="pool">The pool.</param>
    /// <param name="limit">The most work items that run at once, at least 1.</param>
    void SetPoolLimit(ResourcePool pool, int limit);

    /// <summary>
    /// Pauses all processing (E05-S05): no new work starts. Running work gets a short grace period to finish; what's
    /// still running then is cancelled and put back in the queue, so nothing is lost. Pools and jobs paused on their
    /// own stay paused after <see cref="Resume"/>.
    /// </summary>
    void Pause();

    /// <summary>
    /// Resumes all processing after <see cref="Pause"/>, or after the scheduler was started paused.
    /// </summary>
    void Resume();

    /// <summary>
    /// Gets whether a pool is paused on its own, e.g. only AI analysis (E05-S05). It doesn't include <see cref="IsPaused"/>.
    /// </summary>
    /// <param name="pool">The pool.</param>
    /// <returns><see langword="true"/> if the pool is paused.</returns>
    bool IsPoolPaused(ResourcePool pool);

    /// <summary>
    /// Pauses one pool, like <see cref="Pause"/> does for all of them.
    /// </summary>
    /// <param name="pool">The pool.</param>
    void PausePool(ResourcePool pool);

    /// <summary>
    /// Resumes a pool paused with <see cref="PausePool"/>.
    /// </summary>
    /// <param name="pool">The pool.</param>
    void ResumePool(ResourcePool pool);

    /// <summary>
    /// Pauses a job (E05-S05): it goes to <see cref="ProcessingJobState.Paused"/>, and its work is settled like
    /// <see cref="Pause"/> does. Work added to it while it's paused waits too. The state is stored with the job.
    /// </summary>
    /// <param name="jobId">The job.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="false"/> if the job doesn't exist, is finished, or is already paused.</returns>
    Task<bool> PauseJobAsync(Id<ProcessingJob> jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a job paused with <see cref="PauseJobAsync"/>.
    /// </summary>
    /// <param name="jobId">The job.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="false"/> if the job doesn't exist or isn't paused.</returns>
    Task<bool> ResumeJobAsync(Id<ProcessingJob> jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues the parked work again, e.g. after a folder came back Online (D188). Work whose folder or AI provider is
    /// still unavailable is parked again when it runs.
    /// </summary>
    void ReleaseParked();
}
