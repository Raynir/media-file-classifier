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
    /// <summary>
    /// The <see cref="RetryAt"/> of a parked work item: a whole millisecond, so it survives the Unix-milliseconds
    /// storage (D071) unchanged.
    /// </summary>
    public static readonly DateTimeOffset ParkedUntil = new(9999, 12, 31, 0, 0, 0, TimeSpan.Zero);

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
    /// Gets the category of <see cref="LastError"/>, if the work item failed or waits for a retry (E05-S04).
    /// </summary>
    public ErrorCategory? ErrorCategory { get; init; }

    /// <summary>
    /// Gets how many automatic retries the work item used since it was queued or retried by the user (E05-S04-T02).
    /// Restarts and pauses of a failing provider don't count.
    /// </summary>
    public int RetryCount { get; init; }

    /// <summary>
    /// Gets when a queued retry may start, or <see langword="null"/> when it may start at once. A parked work item
    /// (<see cref="IsParked"/>) has <see cref="ParkedUntil"/> here.
    /// </summary>
    public DateTimeOffset? RetryAt { get; init; }

    /// <summary>
    /// Gets whether the work item is parked because the AI provider it needs is unavailable (E05-S04-T03): it starts
    /// again when the provider's circuit closes, not at a time.
    /// </summary>
    public bool IsParked => State == WorkItemState.Queued && RetryAt == ParkedUntil;

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
            WorkItemState.Running => this with { State = state, AttemptCount = AttemptCount + 1, StartedAt = now, FinishedAt = null, LastError = null, ErrorCategory = null, RetryAt = null },

            // Queued again by the user: a fresh set of automatic retries.
            WorkItemState.Queued when State is WorkItemState.Failed or WorkItemState.Cancelled => this with { State = state, FinishedAt = null, RetryCount = 0, RetryAt = null },
            WorkItemState.Queued => this with { State = state, FinishedAt = null, RetryAt = null },
            WorkItemState.Failed => this with { State = state, FinishedAt = now, LastError = error },
            _ => this with { State = state, FinishedAt = now },
        };
    }

    /// <summary>
    /// Fails the running work item for good, with the error's category (E05-S04).
    /// </summary>
    /// <param name="now">Current time.</param>
    /// <param name="category">What kind of failure it was.</param>
    /// <param name="error">Error message.</param>
    /// <returns>The failed work item.</returns>
    /// <exception cref="InvalidOperationException">The work item isn't running.</exception>
    public WorkItem Fail(DateTimeOffset now, ErrorCategory category, string error) =>
        ChangeState(WorkItemState.Failed, now, error) with { ErrorCategory = category };

    /// <summary>
    /// Puts the running work item back in the queue after a failure, to start again at a later time (E05-S04-T02).
    /// </summary>
    /// <param name="retryAt">When it may start again.</param>
    /// <param name="category">What kind of failure it was.</param>
    /// <param name="error">Error message, kept until the retry starts.</param>
    /// <param name="countsAsRetry">
    /// Whether the retry uses up one of the automatic retries; not when a paused provider or a missing model is at fault.
    /// </param>
    /// <param name="now">Current time.</param>
    /// <returns>The queued work item.</returns>
    /// <exception cref="InvalidOperationException">The work item isn't running.</exception>
    public WorkItem ScheduleRetry(DateTimeOffset retryAt, ErrorCategory category, string error, bool countsAsRetry, DateTimeOffset now)
    {
        if (State != WorkItemState.Running)
        {
            throw new InvalidOperationException($"Only a running work item can be retried, not one that's {State}.");
        }

        return ChangeState(WorkItemState.Queued, now) with
        {
            RetryAt = retryAt,
            ErrorCategory = category,
            LastError = error,
            RetryCount = countsAsRetry ? RetryCount + 1 : RetryCount,
        };
    }

    /// <summary>
    /// Puts the running work item back in the queue until the AI provider it needs is available again (E05-S04-T03).
    /// It doesn't use up an automatic retry.
    /// </summary>
    /// <param name="category">What kind of failure it was.</param>
    /// <param name="error">Error message, kept until it starts again.</param>
    /// <param name="now">Current time.</param>
    /// <returns>The parked work item.</returns>
    /// <exception cref="InvalidOperationException">The work item isn't running.</exception>
    public WorkItem Park(ErrorCategory category, string error, DateTimeOffset now) =>
        ScheduleRetry(ParkedUntil, category, error, countsAsRetry: false, now);
}
