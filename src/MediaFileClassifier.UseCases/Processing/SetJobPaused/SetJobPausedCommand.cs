using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediatR;

namespace MediaFileClassifier.UseCases.Processing.SetJobPaused;

/// <summary>
/// Pauses or resumes one job from the processing panel (E05-S05-T02). A paused job starts no new work, and its running
/// work gets a short grace period before it's put back in the queue.
/// </summary>
/// <param name="JobId">The job.</param>
/// <param name="Paused"><see langword="true"/> to pause the job, <see langword="false"/> to resume it.</param>
public sealed record SetJobPausedCommand(Id<ProcessingJob> JobId, bool Paused) : IRequest<bool>;
