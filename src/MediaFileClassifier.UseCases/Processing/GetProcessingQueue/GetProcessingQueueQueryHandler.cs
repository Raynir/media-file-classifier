using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Processing.GetProcessingQueue;

/// <summary>
/// Handles <see cref="GetProcessingQueueQuery"/> for the open library.
/// </summary>
/// <param name="session">Holds the open library.</param>
public sealed class GetProcessingQueueQueryHandler(ILibrarySession session) : IRequestHandler<GetProcessingQueueQuery, ProcessingQueue>
{
    /// <inheritdoc />
    public async Task<ProcessingQueue> Handle(GetProcessingQueueQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (session.Current is not { } library)
        {
            return ProcessingQueue.Empty;
        }

        var unfinished = await library.Jobs.GetUnfinishedJobsAsync(cancellationToken).ConfigureAwait(false);
        var recent = await library.Jobs.GetRecentJobsAsync(Math.Max(1, request.RecentLimit), cancellationToken).ConfigureAwait(false);
        var ids = unfinished.Concat(recent).Select(job => job.Id).ToList();
        var counts = (await library.Jobs.CountWorkAsync(ids, cancellationToken).ConfigureAwait(false)).ToLookup(count => count.JobId);
        var failures = await library.Jobs.CountFailuresAsync(ids, cancellationToken).ConfigureAwait(false);

        QueueJob Describe(ProcessingJob job)
        {
            var done = 0;
            var pending = 0;
            var failed = 0;
            foreach (var count in counts[job.Id])
            {
                switch (count.State)
                {
                    case WorkItemState.Completed:
                        done += count.Count;
                        break;
                    case WorkItemState.Failed:
                        failed += count.Count;
                        break;
                    case WorkItemState.Queued or WorkItemState.Running:
                        pending += count.Count;
                        break;
                }
            }

            return new QueueJob(
                job.Id,
                job.Type,
                job.Description,
                job.State,
                job.CreatedAt,
                job.StartedAt,
                job.FinishedAt,
                done + pending + failed,
                done,
                failures.GetValueOrDefault(job.Id),
                pending);
        }

        return new ProcessingQueue([.. unfinished.Select(Describe)], [.. recent.Select(Describe)]);
    }
}
