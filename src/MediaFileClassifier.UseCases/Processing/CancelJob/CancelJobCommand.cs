using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediatR;

namespace MediaFileClassifier.UseCases.Processing.CancelJob;

/// <summary>
/// Cancels a job from the processing panel (E05-S03-T02): its queued work items are dropped and the running ones are
/// asked to stop. What the job finished already stays.
/// </summary>
/// <param name="JobId">The job to cancel.</param>
public sealed record CancelJobCommand(Id<ProcessingJob> JobId) : IRequest<bool>;
