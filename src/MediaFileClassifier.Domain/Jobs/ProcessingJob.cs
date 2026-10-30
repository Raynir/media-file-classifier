using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Domain.Jobs;

/// <summary>
/// Record of a background job, such as an import or a re-analysis. Structure only: the queue, scheduling and
/// state transitions live in E05.
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
    /// Gets the priority. Higher runs first.
    /// </summary>
    public int Priority { get; init; }

    public int AttemptCount { get; init; }

    public string? LastError { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }
}
