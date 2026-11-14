using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediatR;

namespace MediaFileClassifier.UseCases.Processing.GetJobFailures;

/// <summary>
/// Gets the failures of a job that still need attention, for the processing panel's details (E05-S03-T03).
/// </summary>
/// <param name="JobId">The job.</param>
public sealed record GetJobFailuresQuery(Id<ProcessingJob> JobId) : IRequest<IReadOnlyList<FailedWorkItem>>;

/// <summary>
/// One failure of a job.
/// </summary>
/// <param name="WorkItemId">The failed work item.</param>
/// <param name="ItemId">The media item, or <c>null</c> when the whole target failed, e.g. finding files in a folder.</param>
/// <param name="FileName">The file's name, or <c>null</c> without an item.</param>
/// <param name="RelativePath">The file's path in its folder, or <c>null</c> without an item.</param>
/// <param name="Stage">The stage that failed, e.g. <c>import.fingerprint</c>.</param>
/// <param name="Error">What went wrong, as the stage reported it; may be empty.</param>
/// <param name="Category">The kind of failure, if known.</param>
public sealed record FailedWorkItem(
    Id<WorkItem> WorkItemId,
    Id<MediaItem>? ItemId,
    string? FileName,
    string? RelativePath,
    string Stage,
    string Error,
    ErrorCategory? Category);
