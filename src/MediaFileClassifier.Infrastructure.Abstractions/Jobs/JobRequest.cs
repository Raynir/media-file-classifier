using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Abstractions.Jobs;

/// <summary>
/// A job to enqueue with <see cref="IJobScheduler.EnqueueJobAsync"/>. The target is the whole library unless
/// <see cref="FolderId"/> or <see cref="ItemIds"/> is set.
/// </summary>
/// <param name="Type">Job type key, e.g. <c>import</c>, <c>hash</c> or <c>analyze</c>.</param>
/// <param name="Description">Description shown in the processing queue.</param>
/// <param name="Priority">Priority of the job and all its work.</param>
/// <param name="WorkItems">The first work items. Handlers can add more as the job runs.</param>
public sealed record JobRequest(string Type, string Description, JobPriority Priority, IReadOnlyList<WorkItemRequest> WorkItems)
{
    /// <summary>
    /// Gets the target folder, for folder jobs.
    /// </summary>
    public Id<IndexedFolder>? FolderId { get; init; }

    /// <summary>
    /// Gets the target items, for item-set jobs.
    /// </summary>
    public IReadOnlyList<Id<MediaItem>> ItemIds { get; init; } = [];
}
