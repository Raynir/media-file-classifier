using MediaFileClassifier.Infrastructure.Abstractions.Errors;

namespace MediaFileClassifier.Infrastructure.Abstractions.Jobs;

/// <summary>
/// A stage that records its own failures, e.g. by putting the media item in the Error state (E05-S04). The scheduler
/// retries transient failures and pauses work for a failing provider, so a stage mustn't record a failure in
/// <see cref="IStageHandler.HandleAsync"/>: it lets the exception propagate, and records it here, which is called only
/// once the scheduler gives up on the work item.
/// </summary>
public interface IFailureAwareStage : IStageHandler
{
    /// <summary>
    /// Records that a work item failed for good: a permanent or configuration error, or a transient one that kept
    /// failing after every automatic retry.
    /// </summary>
    /// <param name="context">The work item, as it was running, and its library.</param>
    /// <param name="error">The failure, classified.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the failure is recorded.</returns>
    Task OnFailedAsync(WorkItemContext context, ClassifiedError error, CancellationToken cancellationToken);
}
