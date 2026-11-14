using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Rescan.GetRescanSummary;

/// <summary>
/// Asks how a rescan job is going (E02-S04-T06): the answer is <c>null</c> while the job runs, and the summary once it
/// has finished.
/// </summary>
/// <param name="JobId">The rescan job.</param>
public sealed record GetRescanSummaryQuery(Id<ProcessingJob> JobId) : IRequest<RescanSummary?>;

/// <summary>
/// The result of a finished rescan.
/// </summary>
/// <param name="State">How the job ended: Completed, Failed or Cancelled.</param>
/// <param name="Counts">What reconciliation found, or <c>null</c> if the job ended before it ran.</param>
/// <param name="Updated">Files whose content changed and were refreshed.</param>
public sealed record RescanSummary(ProcessingJobState State, RescanCounts? Counts, int Updated);

/// <summary>
/// Handles <see cref="GetRescanSummaryQuery"/>. "Updated" is the number of files refreshed after the check found a
/// change, which only the check stage knows, so it's the job's <c>rescan.media</c> work items.
/// </summary>
/// <param name="session">Holds the open library.</param>
public sealed class GetRescanSummaryQueryHandler(ILibrarySession session) : IRequestHandler<GetRescanSummaryQuery, RescanSummary?>
{
    /// <inheritdoc />
    public async Task<RescanSummary?> Handle(GetRescanSummaryQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (session.Current is not { } library)
        {
            return null;
        }

        var job = await library.Jobs.GetJobAsync(request.JobId, cancellationToken).ConfigureAwait(false);
        if (job is null || !ProcessingJobStateTransitions.IsFinished(job.State))
        {
            return null;
        }

        var updated = (await library.Jobs.GetWorkItemsAsync(job.Id, cancellationToken).ConfigureAwait(false))
            .Count(workItem => workItem.Stage == RescanStages.Media && workItem.State == WorkItemState.Completed);
        return new RescanSummary(job.State, RescanCounts.FromJson(job.Result), updated);
    }
}
