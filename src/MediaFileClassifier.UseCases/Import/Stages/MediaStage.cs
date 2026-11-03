using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Import.Stages;

/// <summary>
/// Reads an item's technical metadata (E03-S03) and makes its thumbnails (E03-S04), then registers it: it's stored as
/// Pending analysis, <see cref="MediaItemRegistered"/> is published (E03-S07-T05), and its full hash is queued.
/// </summary>
/// <remarks>
/// Thumbnail problems are kept as warnings with the metadata (D128). Only a file that can't be decoded at all, with no
/// thumbnail and no dimensions, fails as a permanent error; a video without FFmpeg isn't one.
/// </remarks>
/// <param name="files">Reads the file.</param>
/// <param name="formats">Detects the file's format, which picks the extractor and thumbnailer.</param>
/// <param name="thumbnails">Makes and stores the thumbnails.</param>
/// <param name="hashJobs">Queues full hashing.</param>
/// <param name="publisher">Publishes <see cref="MediaItemRegistered"/>.</param>
/// <param name="clock">The clock.</param>
/// <param name="logger">Logger.</param>
public sealed partial class MediaStage(
    IMediaFileSource files,
    IMediaFormatRegistry formats,
    IThumbnailCache thumbnails,
    FullHashJobs hashJobs,
    IPublisher publisher,
    TimeProvider clock,
    ILogger<MediaStage> logger) : ItemStage(clock, logger)
{
    /// <inheritdoc />
    public override string Stage => ImportStages.Media;

    /// <inheritdoc />
    public override ResourcePool Pool => ResourcePool.Cpu;

    /// <inheritdoc />
    protected override async Task ProcessAsync(WorkItemContext context, MediaItem item, IndexedFolder folder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(folder);

        // Registered already, e.g. the work item ran again after an interruption.
        if (item.Status is not (MediaItemStatus.Discovered or MediaItemStatus.Error))
        {
            return;
        }

        var location = GetLocation(folder, item);
        MediaFormatDetection format;
        var stream = await files.OpenReadAsync(location, cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            format = await formats.DetectAsync(stream, item.Path.Value, cancellationToken).ConfigureAwait(false);
        }

        if (!format.IsSupported)
        {
            throw new MediaFileException($"The file isn't a supported media file: {format.Rejection}.");
        }

        var content = new MediaContent(item.Id, item.FileModifiedAt, token => files.OpenReadAsync(location, token))
        {
            LocalPath = GetLocalPath(folder, item),
        };
        var metadata = await format.Handler!.MetadataExtractor.ExtractAsync(content, format, cancellationToken).ConfigureAwait(false);
        var thumbnail = await thumbnails.GenerateAsync(context.Library.Layout, content, format, metadata, cancellationToken).ConfigureAwait(false);
        if (!thumbnail.IsStored && metadata.Width is null && thumbnail.Warnings.Any(warning => warning.Code == ThumbnailWarnings.ThumbnailFailed))
        {
            throw new MediaFileException("The file is damaged and can't be decoded.");
        }

        metadata = metadata with { Warnings = [.. metadata.Warnings, .. thumbnail.Warnings] };
        item.ChangeStatus(MediaItemStatus.Pending, Clock.GetUtcNow());
        await context.Library.Items.UpdateAsync(item, metadata, cancellationToken).ConfigureAwait(false);

        // Stored: from here on the item is imported, so nothing that follows marks it as failed. An item whose full hash
        // isn't queued is still found by IMediaItemRepository.GetWithoutContentHashAsync.
        try
        {
            await publisher.Publish(new MediaItemRegistered(item.Id, item.FolderId), CancellationToken.None).ConfigureAwait(false);
            await hashJobs.EnqueueAsync(context.Scheduler, folder, [item.Id], CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogAfterRegistrationFailed(exception, item.Path.Value);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Path} is imported, but announcing it or queueing its full hash failed")]
    private partial void LogAfterRegistrationFailed(Exception exception, string path);
}
