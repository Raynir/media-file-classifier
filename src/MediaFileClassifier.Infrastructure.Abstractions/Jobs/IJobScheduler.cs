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
    /// Gets whether processing waits for <see cref="Resume"/>, e.g. after the library opened with unfinished work
    /// and the settings say to ask first.
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
    /// Starts processing after the scheduler was started paused.
    /// </summary>
    void Resume();
}
