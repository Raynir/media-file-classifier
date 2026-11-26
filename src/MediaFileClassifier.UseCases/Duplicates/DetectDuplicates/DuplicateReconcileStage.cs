using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;

namespace MediaFileClassifier.UseCases.Duplicates.DetectDuplicates;

/// <summary>
/// The stage of <see cref="DuplicateStages.DetectJob"/>: reconciles one batch of queued content hashes in one short
/// write (E12-S01-T03). Batches are taken from the shared queue, so one that the <see cref="DuplicateGroupMaintainer"/>
/// already worked off finds less or nothing to do; running one again is harmless.
/// </summary>
/// <param name="clock">The clock.</param>
public sealed class DuplicateReconcileStage(TimeProvider clock) : IStageHandler
{
    /// <inheritdoc />
    public string Stage => DuplicateStages.Reconcile;

    /// <inheritdoc />
    public ResourcePool Pool => ResourcePool.Io;

    /// <inheritdoc />
    public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        await context.Library.Duplicates.ReconcileAsync(DuplicateGroupMaintainer.BatchSize, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
    }
}
