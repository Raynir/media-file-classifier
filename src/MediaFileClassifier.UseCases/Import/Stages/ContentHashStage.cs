using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Hashing;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Import.Stages;

/// <summary>
/// Computes an imported item's full content hash and, for images, its perceptual hash (E01-S06-T02). Items that share
/// a content hash are exact duplicate candidates (E03-S07-T06): the indexed hash column records them, and
/// <c>IMediaItemRepository.GetExactDuplicatesAsync</c> lists them for E12.
/// </summary>
/// <param name="hasher">Computes the hashes.</param>
/// <param name="files">Checks the folder's root when a file's directory is gone.</param>
/// <param name="clock">The clock.</param>
/// <param name="logger">Logger.</param>
public sealed class ContentHashStage(IMediaHasher hasher, IMediaFileSource files, TimeProvider clock, ILogger<ContentHashStage> logger) : ItemStage(clock, logger)
{
    /// <inheritdoc />
    protected override IMediaFileSource Files => files;

    /// <inheritdoc />
    public override string Stage => ImportStages.ContentHash;

    /// <inheritdoc />
    public override ResourcePool Pool => ResourcePool.Io;

    /// <inheritdoc />
    protected override async Task ProcessAsync(WorkItemContext context, MediaItem item, IndexedFolder folder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(item);

        // An item that failed here before is retried; one that failed in another stage isn't hashed.
        var retried = item.Status == MediaItemStatus.Error && item.Error?.Stage == Stage;
        if (!retried && (item.Hashes.ContentHash is not null || item.Status == MediaItemStatus.Error))
        {
            return;
        }

        var hashes = await hasher.ComputeMissingAsync(GetLocation(folder, item), item.SizeBytes, item.MediaType, item.Hashes, cancellationToken).ConfigureAwait(false);
        item.SetHashes(hashes);

        // The item was imported before it failed here.
        if (retried)
        {
            item.ChangeStatus(MediaItemStatus.Pending, Clock.GetUtcNow());
        }

        await context.Library.Items.UpdateAsync(item, cancellationToken).ConfigureAwait(false);
    }
}
