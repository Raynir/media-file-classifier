using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.UseCases.Analysis.Pipeline;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Analysis.AnalyzeItems;

/// <summary>
/// Handles <see cref="AnalyzeItemsCommand"/>.
/// </summary>
/// <param name="jobs">Runs the jobs of the open library.</param>
/// <param name="logger">Logger.</param>
public sealed partial class AnalyzeItemsCommandHandler(IJobSystem jobs, ILogger<AnalyzeItemsCommandHandler> logger)
    : IRequestHandler<AnalyzeItemsCommand, Id<ProcessingJob>?>
{
    /// <inheritdoc />
    /// <returns>The job, or <c>null</c> when there's nothing to analyze or background processing isn't running.</returns>
    public async Task<Id<ProcessingJob>?> Handle(AnalyzeItemsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ItemIds);

        var perItem = request.ItemAnalyzers;
        var itemIds = request.ItemIds.Distinct()
            .Where(itemId => perItem is null || !perItem.TryGetValue(itemId, out var analyzers) || analyzers.Count > 0)
            .ToList();
        if (itemIds.Count == 0 || (request.Analyzers is { Count: 0 } && perItem is null))
        {
            return null;
        }

        if (jobs.Current is not { } scheduler)
        {
            LogNotRunning(itemIds.Count);
            return null;
        }

        var payload = AnalysisPayload.Serialize(request.Analyzers);
        var work = itemIds.Select(itemId => new WorkItemRequest(
            AnalysisStages.Item,
            itemId,
            perItem is not null && perItem.TryGetValue(itemId, out var analyzers) ? AnalysisPayload.Serialize(analyzers) : payload)).ToList();
        var description = request.Description ?? (itemIds.Count == 1 ? "Analyze 1 item" : $"Analyze {itemIds.Count} items");
        return await scheduler.EnqueueJobAsync(
            new JobRequest(AnalysisStages.AnalysisJob, description, request.Priority, work) { ItemIds = itemIds },
            cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Background processing isn't running, so {Count} items aren't analyzed")]
    private partial void LogNotRunning(int count);
}
