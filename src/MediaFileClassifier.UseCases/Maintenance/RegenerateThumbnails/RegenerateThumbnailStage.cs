using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Import.Stages;
using MediaFileClassifier.UseCases.Libraries;

namespace MediaFileClassifier.UseCases.Maintenance.RegenerateThumbnails;

/// <summary>
/// Makes one item's thumbnails again when one is missing or older than its file (E02-S06-T02). It writes only the
/// thumbnail files: the item, its metadata and its embeddings stay as they are, and nothing is analyzed. A failure
/// fails the work item but leaves the item's status alone, so the item keeps its placeholder.
/// </summary>
/// <param name="files">Reads the file.</param>
/// <param name="formats">Detects the file's format, which picks the thumbnailer.</param>
/// <param name="thumbnails">Finds, makes and stores the thumbnails.</param>
public sealed class RegenerateThumbnailStage(IMediaFileSource files, IMediaFormatRegistry formats, IThumbnailCache thumbnails) : IStageHandler
{
    /// <inheritdoc />
    public string Stage => MaintenanceJobs.ThumbnailsStage;

    /// <inheritdoc />
    public ResourcePool Pool => ResourcePool.Cpu;

    /// <summary>
    /// Tells whether an item is missing a thumbnail, or has one older than its file.
    /// </summary>
    /// <param name="thumbnails">The thumbnail cache.</param>
    /// <param name="library">The library.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="fileModifiedAt">The item's file modification time.</param>
    /// <returns>Whether the item needs its thumbnails made again.</returns>
    public static bool NeedsThumbnails(IThumbnailCache thumbnails, LibraryLayout library, Id<MediaItem> itemId, DateTimeOffset fileModifiedAt)
    {
        ArgumentNullException.ThrowIfNull(thumbnails);

        return Enum.GetValues<ThumbnailSize>().Any(size => thumbnails.Find(library, itemId, size, fileModifiedAt) is null);
    }

    /// <inheritdoc />
    public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var itemId = context.WorkItem.ItemId ?? throw new InvalidOperationException($"A {Stage} work item needs an item.");
        var library = context.Library;
        var item = await library.Items.GetAsync(itemId, cancellationToken).ConfigureAwait(false);

        // Removed, gone or excluded since the work was queued, or made again meanwhile (e.g. after a restart).
        if (item is null || !MaintenanceJobs.HasThumbnails(item.Status) || !NeedsThumbnails(thumbnails, library.Layout, item.Id, item.FileModifiedAt))
        {
            return;
        }

        var folder = await library.Folders.GetAsync(item.FolderId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The item's folder isn't in the library.");

        // Offline: the work is parked and runs again when the folder is back (D188).
        OfflineFolders.ThrowIfOffline(folder);
        var location = ItemStage.GetLocation(folder, item);
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
            LocalPath = ItemStage.GetLocalPath(folder, item),
        };

        // The stored metadata, so nothing is read twice and nothing is written back.
        var metadata = await library.Items.GetTechnicalMetadataAsync(item.Id, cancellationToken).ConfigureAwait(false)
            ?? await format.Handler!.MetadataExtractor.ExtractAsync(content, format, cancellationToken).ConfigureAwait(false);
        var outcome = await thumbnails.GenerateAsync(library.Layout, content, format, metadata, cancellationToken).ConfigureAwait(false);
        if (!outcome.IsStored)
        {
            throw new MediaFileException(outcome.Warnings.FirstOrDefault()?.Detail ?? "No thumbnail could be made from the file.");
        }
    }
}
