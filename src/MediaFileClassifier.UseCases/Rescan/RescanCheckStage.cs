using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Hashing;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.UseCases.Import.Stages;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Rescan;

/// <summary>
/// Decides whether a modified or returning file's content changed (E02-S04-T03). The content changed when the size or
/// the fingerprint differs; with both the same, an item with a full content hash is hashed again to compare. A changed
/// item gets the new facts and fingerprint, loses its other hashes and thumbnails, goes back to Pending, and is queued for
/// <see cref="RefreshMediaStage"/>. An unchanged one only gets the new times, and a returning one its status back.
/// </summary>
/// <param name="files">Reads the file's facts.</param>
/// <param name="hasher">Computes the fingerprint and, when needed, the content hash.</param>
/// <param name="thumbnails">Drops a changed file's thumbnails.</param>
/// <param name="clock">Clock for status changes.</param>
/// <param name="logger">Logger.</param>
public sealed class RescanCheckStage(
    IMediaFileSource files,
    IMediaHasher hasher,
    IThumbnailCache thumbnails,
    TimeProvider clock,
    ILogger<RescanCheckStage> logger) : ItemStage(clock, logger)
{
    /// <inheritdoc />
    protected override IMediaFileSource Files => files;

    /// <inheritdoc />
    public override string Stage => RescanStages.Check;

    /// <inheritdoc />
    public override ResourcePool Pool => ResourcePool.Io;

    /// <inheritdoc />
    protected override async Task ProcessAsync(WorkItemContext context, MediaItem item, IndexedFolder folder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(folder);

        var location = GetLocation(folder, item);

        // Gone again since the rescan listed it: the next rescan marks it.
        if (await files.GetStatAsync(location, cancellationToken).ConfigureAwait(false) is not { } stat)
        {
            return;
        }

        var fingerprint = await hasher.ComputeFingerprintAsync(location, stat.Length, cancellationToken).ConfigureAwait(false);
        var changed = stat.Length != item.SizeBytes || fingerprint != item.Hashes.Fingerprint;
        var hashes = item.Hashes;
        if (!changed && item.Hashes.ContentHash is { } contentHash)
        {
            // A fingerprint samples the file; an edit between its chunks only shows in the full hash.
            var fresh = await hasher.ComputeMissingAsync(location, stat.Length, item.MediaType, new FileHashes { Fingerprint = fingerprint }, cancellationToken)
                .ConfigureAwait(false);
            changed = fresh.ContentHash != contentHash;
            hashes = changed ? hashes : fresh;
        }

        var library = context.Library;
        var created = stat.CreatedUtc ?? stat.LastModifiedUtc;
        if (changed)
        {
            item.UpdateFile(stat.Length, created, stat.LastModifiedUtc, new FileHashes { Fingerprint = fingerprint });
            item.ChangeStatus(MediaItemStatus.Pending, Clock.GetUtcNow());
            thumbnails.Invalidate(library.Layout, item.Id);
            await library.Items.UpdateAsync(item, cancellationToken).ConfigureAwait(false);
            await context.Scheduler.EnqueueWorkAsync(context.WorkItem.JobId, [new WorkItemRequest(RescanStages.Media, item.Id)], cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        item.UpdateFile(stat.Length, created, stat.LastModifiedUtc, hashes);
        var refresh = false;
        if (item.Status == MediaItemStatus.Pending)
        {
            // A returning item (Pending since the rescan) gets back what it had: Indexed once analysed, and its
            // metadata and thumbnails if it never got them.
            if (await library.Items.GetTechnicalMetadataAsync(item.Id, cancellationToken).ConfigureAwait(false) is null)
            {
                refresh = true;
            }
            else if ((await library.Metadata.GetAsync(item.Id, cancellationToken).ConfigureAwait(false)).Runs.Any(run => run.ProvidesAiLayer))
            {
                item.ChangeStatus(MediaItemStatus.Indexed, Clock.GetUtcNow());
            }
            else if (thumbnails.Find(library.Layout, item.Id, ThumbnailSize.Small, DateTimeOffset.MinValue) is null)
            {
                // Restored after the user removed it, which deleted its thumbnails and AI results (E02-S10): the media
                // stage makes the thumbnails again and announces it for analysis.
                refresh = true;
            }
        }

        await library.Items.UpdateAsync(item, cancellationToken).ConfigureAwait(false);
        if (refresh)
        {
            await context.Scheduler.EnqueueWorkAsync(context.WorkItem.JobId, [new WorkItemRequest(RescanStages.Media, item.Id)], cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
