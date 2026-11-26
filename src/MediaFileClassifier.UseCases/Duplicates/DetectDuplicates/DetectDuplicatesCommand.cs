using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediatR;

namespace MediaFileClassifier.UseCases.Duplicates.DetectDuplicates;

/// <summary>
/// Runs a full duplicate detection pass over the open library as a background job (E12-S01-T03): every content
/// hash that member items share, and every existing exact group's hash, is queued, and the job reconciles the queue
/// in batches. Then every item with a perceptual hash or keyframes is checked for visual near-duplicates the same
/// way (E12-S02-T04). It repairs groups that drifted, e.g. after a restore from a backup or a sensitivity change.
/// </summary>
public sealed record DetectDuplicatesCommand : IRequest<DetectDuplicatesResult>;

/// <summary>
/// What <see cref="DetectDuplicatesCommand"/> did.
/// </summary>
/// <param name="Outcome">Whether a job was queued.</param>
/// <param name="Hashes">The content hashes queued for the pass.</param>
/// <param name="JobId">The job, when one was queued.</param>
/// <param name="Items">The items queued for the visual check.</param>
public sealed record DetectDuplicatesResult(DetectDuplicatesOutcome Outcome, int Hashes = 0, Id<ProcessingJob>? JobId = null, int Items = 0);

/// <summary>
/// Outcomes of <see cref="DetectDuplicatesCommand"/>.
/// </summary>
public enum DetectDuplicatesOutcome
{
    /// <summary>
    /// The job was queued.
    /// </summary>
    Started,

    /// <summary>
    /// No two items share a content hash, no item has a perceptual hash or keyframes, and there are no groups: nothing
    /// to do.
    /// </summary>
    NothingToDo,

    /// <summary>
    /// A detection pass is already queued or running.
    /// </summary>
    Busy,

    /// <summary>
    /// No library is open.
    /// </summary>
    Unavailable,
}
