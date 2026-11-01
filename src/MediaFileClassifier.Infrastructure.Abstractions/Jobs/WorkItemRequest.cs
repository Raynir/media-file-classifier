using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Abstractions.Jobs;

/// <summary>
/// Work to enqueue: one stage for one media item, or for the job's whole target.
/// </summary>
/// <param name="Stage">Stage key of a registered <see cref="IStageHandler"/>.</param>
/// <param name="ItemId">The media item, if the work is for one.</param>
/// <param name="Payload">Stage-specific input as JSON, if the stage needs more than the item.</param>
public sealed record WorkItemRequest(string Stage, Id<MediaItem>? ItemId = null, string? Payload = null);
