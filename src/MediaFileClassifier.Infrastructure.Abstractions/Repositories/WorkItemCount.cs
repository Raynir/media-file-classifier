using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;

namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// The number of work items of one job, stage and state, for the progress counters (E05-S02-T01).
/// </summary>
/// <param name="JobId">The job.</param>
/// <param name="Stage">Stage key.</param>
/// <param name="State">Work item state.</param>
/// <param name="IsWholeTarget">Whether the work items are for the job's whole target, with no media item.</param>
/// <param name="Count">The number of work items.</param>
public sealed record WorkItemCount(Id<ProcessingJob> JobId, string Stage, WorkItemState State, bool IsWholeTarget, int Count);
