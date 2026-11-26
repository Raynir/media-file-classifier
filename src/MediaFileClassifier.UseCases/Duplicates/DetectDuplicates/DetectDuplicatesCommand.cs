using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediatR;

namespace MediaFileClassifier.UseCases.Duplicates.DetectDuplicates;

/// <summary>
/// Runs a full exact duplicate detection pass over the open library as a background job (E12-S01-T03): every content
/// hash that member items share, and every existing exact group's hash, is queued, and the job reconciles the queue
/// in batches. It repairs groups that drifted, e.g. after a restore from a backup.
/// </summary>
public sealed record DetectDuplicatesCommand : IRequest<DetectDuplicatesResult>;

/// <summary>
/// What <see cref="DetectDuplicatesCommand"/> did.
/// </summary>
/// <param name="Outcome">Whether a job was queued.</param>
/// <param name="Hashes">The content hashes queued for the pass.</param>
/// <param name="JobId">The job, when one was queued.</param>
public sealed record DetectDuplicatesResult(DetectDuplicatesOutcome Outcome, int Hashes = 0, Id<ProcessingJob>? JobId = null);

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
    /// No two items share a content hash and there are no groups: nothing to do.
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
