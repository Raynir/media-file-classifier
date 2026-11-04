using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;

namespace MediaFileClassifier.Infrastructure.Abstractions.Jobs;

/// <summary>
/// What a stage handler gets for a work item.
/// </summary>
/// <param name="WorkItem">The work item, running.</param>
/// <param name="Scheduler">The scheduler of the library, e.g. to enqueue the next stage with <see cref="IJobScheduler.EnqueueWorkAsync"/>.</param>
public sealed record WorkItemContext(WorkItem WorkItem, IJobScheduler Scheduler)
{
    /// <summary>
    /// Gets the library the work is for. It stays open until the handler returns.
    /// </summary>
    public IOpenLibrary Library => Scheduler.Library;
}
