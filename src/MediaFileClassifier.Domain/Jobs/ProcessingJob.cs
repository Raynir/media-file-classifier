using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Domain.Jobs;

/// <summary>
/// Record of a background job, such as an import or a re-analysis (E05-S01). Its work is split into
/// <see cref="WorkItem"/>s, and its state follows <see cref="ProcessingJobStateTransitions"/>.
/// </summary>
/// <remarks>
/// The target is the whole library when neither <see cref="FolderId"/> nor <see cref="ItemIds"/> is set.
/// </remarks>
public sealed record ProcessingJob
{
    public required Id<ProcessingJob> Id { get; init; }

    /// <summary>
    /// Gets the job type key, e.g. <c>import</c>, <c>hash</c>, <c>thumbnail</c>, <c>analyze</c> or <c>embed</c>. E05 defines the set.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Gets the description shown in the processing queue.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets the target folder, for folder jobs.
    /// </summary>
    public Id<IndexedFolder>? FolderId { get; init; }

    /// <summary>
    /// Gets the target items, for item-set jobs.
    /// </summary>
    public IReadOnlyList<Id<MediaItem>> ItemIds { get; init; } = [];

    public ProcessingJobState State { get; init; } = ProcessingJobState.Queued;

    /// <summary>
    /// Gets the priority, a <see cref="JobPriority"/> value. Higher runs first.
    /// </summary>
    public int Priority { get; init; }

    public int AttemptCount { get; init; }

    public string? LastError { get; init; }

    /// <summary>
    /// Gets what the job tells the UI when it finishes, as JSON, e.g. the counts of a rescan (E02-S04-T06), or <c>null</c>.
    /// </summary>
    public string? Result { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>
    /// Returns the job in a new state. Starting records the start time once; finishing records the finish time,
    /// and going back to <see cref="ProcessingJobState.Queued"/> clears it.
    /// </summary>
    /// <param name="state">New state.</param>
    /// <param name="now">Current time.</param>
    /// <param name="error">Error message, when it fails.</param>
    /// <returns>The changed job.</returns>
    /// <exception cref="InvalidOperationException">The change isn't allowed by <see cref="ProcessingJobStateTransitions"/>.</exception>
    public ProcessingJob ChangeState(ProcessingJobState state, DateTimeOffset now, string? error = null)
    {
        if (!ProcessingJobStateTransitions.IsAllowed(State, state))
        {
            throw new InvalidOperationException($"Processing job state can't change from {State} to {state}.");
        }

        return this with
        {
            State = state,
            StartedAt = state == ProcessingJobState.Running ? StartedAt ?? now : StartedAt,
            FinishedAt = ProcessingJobStateTransitions.IsFinished(state) ? now : null,
            LastError = state == ProcessingJobState.Failed ? error : LastError,
        };
    }
}
