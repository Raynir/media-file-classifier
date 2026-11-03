using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Hashing;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Import.Stages;

/// <summary>
/// Computes a discovered item's quick fingerprint (E01-S06-T01), then queues <see cref="ImportStages.Media"/>.
/// </summary>
/// <param name="hasher">Computes the fingerprint.</param>
/// <param name="clock">The clock.</param>
/// <param name="logger">Logger.</param>
public sealed class FingerprintStage(IMediaHasher hasher, TimeProvider clock, ILogger<FingerprintStage> logger) : ItemStage(clock, logger)
{
    /// <inheritdoc />
    public override string Stage => ImportStages.Fingerprint;

    /// <inheritdoc />
    public override ResourcePool Pool => ResourcePool.Io;

    /// <inheritdoc />
    protected override async Task ProcessAsync(WorkItemContext context, MediaItem item, IndexedFolder folder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(item);

        if (item.Hashes.Fingerprint is null)
        {
            var fingerprint = await hasher.ComputeFingerprintAsync(GetLocation(folder, item), item.SizeBytes, cancellationToken).ConfigureAwait(false);
            item.SetHashes(item.Hashes with { Fingerprint = fingerprint });
            await context.Library.Items.UpdateAsync(item, cancellationToken).ConfigureAwait(false);
        }

        await context.Scheduler.EnqueueWorkAsync(context.WorkItem.JobId, [new WorkItemRequest(ImportStages.Media, item.Id)], cancellationToken).ConfigureAwait(false);
    }
}
