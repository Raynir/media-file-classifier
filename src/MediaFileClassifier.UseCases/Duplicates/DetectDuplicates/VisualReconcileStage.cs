using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.UseCases.Duplicates.Visual;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.UseCases.Duplicates.DetectDuplicates;

/// <summary>
/// The visual stage of <see cref="DuplicateStages.DetectJob"/>: checks one batch of items queued for visual
/// near-duplicates (E12-S02-T04). Batches come from the shared queue, so one that the
/// <see cref="DuplicateGroupMaintainer"/> already worked off finds less or nothing to do. While the vector index is
/// still loading, the batch stays queued, and the maintainer checks it once the index is ready.
/// </summary>
/// <remarks>
/// The detector is resolved when the first batch runs, not with the stage: it needs the search index, which needs the
/// library session, which needs the job system and so every stage. Resolving it with the stage was a dependency cycle
/// that hung the app at startup.
/// </remarks>
/// <param name="services">Resolves the <see cref="VisualDuplicateDetector"/>.</param>
public sealed class VisualReconcileStage(IServiceProvider services) : IStageHandler
{
    private VisualDuplicateDetector? detector;

    /// <inheritdoc />
    public string Stage => DuplicateStages.VisualReconcile;

    /// <inheritdoc />
    public ResourcePool Pool => ResourcePool.Io;

    /// <inheritdoc />
    public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        detector ??= services.GetRequiredService<VisualDuplicateDetector>();
        await detector.ReconcileAsync(context.Library, VisualDuplicateDetector.BatchSize, cancellationToken).ConfigureAwait(false);
    }
}
