using MediaFileClassifier.Domain.Jobs;

namespace MediaFileClassifier.Infrastructure.Abstractions.Jobs;

/// <summary>
/// Runs one stage of work for one work item, e.g. the thumbnail of one media item (E05-S01-T08). Register handlers
/// in dependency injection as <see cref="IStageHandler"/>; the scheduler picks one by <see cref="Stage"/>.
/// </summary>
/// <remarks>
/// A handler commits its result in one write, at the end, so a cancelled or failed work item leaves no partial result.
/// It passes the cancellation token on to everything slow (file reads, ONNX, HTTP, FFmpeg): the token is cancelled
/// when the job is cancelled, or 2 seconds after the library starts closing. A handler that throws fails the work
/// item with the exception's message.
/// </remarks>
public interface IStageHandler
{
    /// <summary>
    /// Gets the stage key, e.g. <c>fingerprint</c>. It's stored with each work item, so keep it stable.
    /// </summary>
    string Stage { get; }

    /// <summary>
    /// Gets the resource pool the stage runs in.
    /// </summary>
    ResourcePool Pool { get; }

    /// <summary>
    /// Runs the stage for a work item.
    /// </summary>
    /// <param name="context">The work item, its library, and a way to enqueue follow-up work.</param>
    /// <param name="cancellationToken">Cancelled when the job is cancelled or the library closes.</param>
    /// <returns>A task that completes when the stage's result is committed.</returns>
    Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken);
}
