using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Domain.Jobs;

/// <summary>
/// One stage of a job for one target, e.g. "thumbnail" for one media item (E05-S01). Work items are persisted, so
/// unfinished work resumes after a restart. A stage handler commits its result atomically, so a cancelled work item
/// leaves no partial result.
/// </summary>
public sealed record WorkItem
{
    public required Id<WorkItem> Id { get; init; }

    public required Id<ProcessingJob> JobId { get; init; }

    /// <summary>
    /// Gets the media item the work is for, or <see langword="null"/> for work on the job's whole target, e.g. discovering a folder.
    /// </summary>
    public Id<MediaItem>? ItemId { get; init; }

    /// <summary>
    /// Gets the stage key, which selects the stage handler, e.g. <c>fingerprint</c> or <c>thumbnail</c>.
    /// </summary>
    public required string Stage { get; init; }

    public required ResourcePool Pool { get; init; }

    /// <summary>
    /// Gets the priority, a <see cref="JobPriority"/> value. Higher runs first.
    /// </summary>
    public required int Priority { get; init; }

    public WorkItemState State { get; init; } = WorkItemState.Queued;

    /// <summary>
    /// Gets how many times the work item started.
    /// </summary>
    public int AttemptCount { get; init; }

    public string? LastError { get; init; }

    /// <summary>
    /// Gets stage-specific input as JSON, if the stage needs more than the item, e.g. a folder path to discover.
    /// </summary>
    public string? Payload { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>
    /// Creates a queued work item.
    /// </summary>
    /// <param name="jobId">The job it belongs to.</param>
    /// <param name="itemId">The media item it's for, if any.</param>
    /// <param name="stage">Stage key.</param>
    /// <param name="pool">Resource pool of the stage.</param>
    /// <param name="priority">Priority.</param>
    /// <param name="payload">Stage-specific input as JSON, if any.</param>
    /// <param name="now">Current time.</param>
    /// <returns>The work item.</returns>
    public static WorkItem Create(
        Id<ProcessingJob> jobId,
        Id<MediaItem>? itemId,
        string stage,
        ResourcePool pool,
        int priority,
        string? payload,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);

        return new WorkItem
        {
            Id = Id<WorkItem>.New(),
            JobId = jobId,
            ItemId = itemId,
            Stage = stage,
            Pool = pool,
            Priority = priority,
            Payload = payload,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Returns the work item in a new state, with the times and attempt count that go with it: starting counts an
    /// attempt and clears the last error, failing records the error, and finishing records the time.
    /// </summary>
    /// <param name="state">New state.</param>
    /// <param name="now">Current time.</param>
    /// <param name="error">Error message, when it fails.</param>
    /// <returns>The changed work item.</returns>
    /// <exception cref="InvalidOperationException">The change isn't allowed by <see cref="WorkItemStateTransitions"/>.</exception>
    public WorkItem ChangeState(WorkItemState state, DateTimeOffset now, string? error = null)
    {
        if (!WorkItemStateTransitions.IsAllowed(State, state))
        {
            throw new InvalidOperationException($"Work item state can't change from {State} to {state}.");
        }

        return state switch
        {
            WorkItemState.Running => this with { State = state, AttemptCount = AttemptCount + 1, StartedAt = now, FinishedAt = null, LastError = null },
            WorkItemState.Queued => this with { State = state, FinishedAt = null },
            WorkItemState.Failed => this with { State = state, FinishedAt = now, LastError = error },
            _ => this with { State = state, FinishedAt = now },
        };
    }
}
