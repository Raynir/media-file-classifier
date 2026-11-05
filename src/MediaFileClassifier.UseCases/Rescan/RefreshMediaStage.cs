using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Import.Stages;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Rescan;

/// <summary>
/// Refreshes a changed file (E02-S04-T03): the import's media stage, run again for an item that's registered already.
/// It replaces the technical metadata and thumbnails, queues the full hash, and publishes
/// <see cref="MediaItemRegistered"/> with <see cref="MediaItemRegistered.ContentChanged"/> set, so analysis runs again.
/// User values aren't touched.
/// </summary>
/// <param name="files">Reads the file.</param>
/// <param name="formats">Detects the format.</param>
/// <param name="thumbnails">Makes the thumbnails.</param>
/// <param name="hashJobs">Queues the full hash.</param>
/// <param name="publisher">Publishes the notification.</param>
/// <param name="clock">Clock for status changes.</param>
/// <param name="logger">Logger.</param>
public sealed class RefreshMediaStage(
    IMediaFileSource files,
    IMediaFormatRegistry formats,
    IThumbnailCache thumbnails,
    FullHashJobs hashJobs,
    IPublisher publisher,
    TimeProvider clock,
    ILogger<RefreshMediaStage> logger) : MediaStage(files, formats, thumbnails, hashJobs, publisher, clock, logger)
{
    /// <inheritdoc />
    public override string Stage => RescanStages.Media;

    /// <inheritdoc />
    /// <remarks><see cref="RescanCheckStage"/> sets a changed item to Pending; one that's Indexed since was done by other means.</remarks>
    protected override bool ShouldProcess(MediaItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Status is MediaItemStatus.Pending or MediaItemStatus.Error;
    }
}
