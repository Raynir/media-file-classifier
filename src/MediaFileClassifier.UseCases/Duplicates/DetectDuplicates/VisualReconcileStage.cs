using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.UseCases.Duplicates.Visual;

namespace MediaFileClassifier.UseCases.Duplicates.DetectDuplicates;

/// <summary>
/// The visual stage of <see cref="DuplicateStages.DetectJob"/>: checks one batch of items queued for visual
/// near-duplicates (E12-S02-T04). Batches come from the shared queue, so one that the
/// <see cref="DuplicateGroupMaintainer"/> already worked off finds less or nothing to do. While the vector index is
/// still loading, the batch stays queued, and the maintainer checks it once the index is ready.
/// </summary>
/// <param name="detector">Visual near-duplicate detection.</param>
public sealed class VisualReconcileStage(VisualDuplicateDetector detector) : IStageHandler
{
    /// <inheritdoc />
    public string Stage => DuplicateStages.VisualReconcile;

    /// <inheritdoc />
    public ResourcePool Pool => ResourcePool.Io;

    /// <inheritdoc />
    public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        await detector.ReconcileAsync(context.Library, VisualDuplicateDetector.BatchSize, cancellationToken).ConfigureAwait(false);
    }
}
