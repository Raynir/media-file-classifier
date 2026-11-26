using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.UseCases.Duplicates.Visual;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Duplicates.DetectDuplicates;

/// <summary>
/// Handles <see cref="DetectDuplicatesCommand"/>.
/// </summary>
/// <param name="logger">Logger.</param>
/// <param name="jobs">The job system; without it, nothing is queued.</param>
public sealed partial class DetectDuplicatesCommandHandler(
    ILogger<DetectDuplicatesCommandHandler> logger,
    IJobSystem? jobs = null) : IRequestHandler<DetectDuplicatesCommand, DetectDuplicatesResult>
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <inheritdoc />
    public async Task<DetectDuplicatesResult> Handle(DetectDuplicatesCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (jobs?.Current is not { } scheduler)
        {
            return new(DetectDuplicatesOutcome.Unavailable);
        }

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var library = scheduler.Library;
            var unfinished = await library.Jobs.GetUnfinishedJobsAsync(cancellationToken).ConfigureAwait(false);
            if (unfinished.Any(job => job.Type == DuplicateStages.DetectJob))
            {
                return new(DetectDuplicatesOutcome.Busy);
            }

            var queued = await library.Duplicates.QueueAllAsync(cancellationToken).ConfigureAwait(false);
            var items = await library.VisualDuplicates.QueueAllAsync(cancellationToken).ConfigureAwait(false);
            if (queued == 0 && items == 0)
            {
                return new(DetectDuplicatesOutcome.NothingToDo);
            }

            // One work item per batch, so the job shows progress and a cancel stops between batches. Exact groups
            // come first; the visual batches follow (E12-S02-T04).
            var batches = (queued + DuplicateGroupMaintainer.BatchSize - 1) / DuplicateGroupMaintainer.BatchSize;
            var visualBatches = (items + VisualDuplicateDetector.BatchSize - 1) / VisualDuplicateDetector.BatchSize;
            var jobId = await scheduler.EnqueueJobAsync(
                new JobRequest(
                    DuplicateStages.DetectJob,
                    "Find duplicates",
                    JobPriority.Maintenance,
                    [
                        .. Enumerable.Range(0, batches).Select(_ => new WorkItemRequest(DuplicateStages.Reconcile)),
                        .. Enumerable.Range(0, visualBatches).Select(_ => new WorkItemRequest(DuplicateStages.VisualReconcile)),
                    ]),
                cancellationToken).ConfigureAwait(false);
            LogQueued(queued, items, batches + visualBatches, library.Library.Name);
            return new(DetectDuplicatesOutcome.Started, queued, jobId, items);
        }
        finally
        {
            Gate.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Queued duplicate detection of {Hashes} content hashes and {Items} items in {Batches} batches for {Library}")]
    private partial void LogQueued(int hashes, int items, int batches, string library);
}
