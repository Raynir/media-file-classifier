namespace MediaFileClassifier.Infrastructure.Abstractions.Jobs;

/// <summary>
/// The progress of the open library's jobs (E05-S02-T01): counters per job and stage, an estimated time remaining,
/// and a summary. It follows the job system's work items, so producers don't report anything themselves.
/// </summary>
/// <remarks>
/// <see cref="Changed"/> is raised on the thread that changed a work item, as often as work items change, so don't
/// update the UI from it directly: the UI samples <see cref="Current"/> at a fixed interval instead (E05-S02-T02).
/// </remarks>
public interface IJobProgress
{
    /// <summary>
    /// Raised on a background thread after a work item was added, started or finished, or a job ended.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Raised on a background thread once per job when it has run to its end and none of its work is pending (not when
    /// it's cancelled, or when the library closes). Work added to the job later makes it finish, and raise this, again.
    /// </summary>
    event EventHandler<JobFinishedEventArgs>? JobFinished;

    /// <summary>
    /// Gets a snapshot of the progress now; <see cref="ProgressSummary.Empty"/> when no library is open.
    /// </summary>
    ProgressSummary Current { get; }
}
