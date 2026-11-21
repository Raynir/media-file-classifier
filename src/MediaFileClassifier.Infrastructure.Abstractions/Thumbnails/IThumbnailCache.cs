using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;

namespace MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;

/// <summary>
/// The thumbnail files of a library (E03-S04-T04), at <c>thumbnails/xx/&lt;item-id&gt;-&lt;size&gt;.webp</c> (D078).
/// </summary>
public interface IThumbnailCache
{
    /// <summary>
    /// Finds an item's thumbnail. Constant time: the path comes from the item ID, and one file check follows.
    /// </summary>
    /// <param name="library">The library.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="size">The size.</param>
    /// <param name="sourceModifiedAt">The item's file modification time. A thumbnail written before it is stale.</param>
    /// <returns>The thumbnail's path, or <see langword="null"/> when there is none or it is stale.</returns>
    string? Find(LibraryLayout library, Id<MediaItem> itemId, ThumbnailSize size, DateTimeOffset sourceModifiedAt);

    /// <summary>
    /// Makes an item's thumbnails with its format handler's generator and stores them, replacing any old ones. Runs
    /// off the calling thread, with a limited number of generations at a time. A failure deletes the old thumbnails
    /// and returns the warning, so the item shows a placeholder. The keyframe pictures of the item's strip stay: only
    /// <see cref="Invalidate"/> removes them.
    /// </summary>
    /// <param name="library">The library.</param>
    /// <param name="content">The file.</param>
    /// <param name="format">The detected format; it must be supported.</param>
    /// <param name="metadata">The item's technical metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether thumbnails were stored, and the warnings.</returns>
    Task<ThumbnailOutcome> GenerateAsync(
        LibraryLayout library,
        MediaContent content,
        MediaFormatDetection format,
        TechnicalMetadata metadata,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes an item's thumbnails and its keyframe pictures, e.g. when its file changed (E02-S04-T03) or the item was removed. Regenerating
    /// (E02-S06-T02) is another <see cref="GenerateAsync"/>.
    /// </summary>
    /// <param name="library">The library.</param>
    /// <param name="itemId">The item.</param>
    void Invalidate(LibraryLayout library, Id<MediaItem> itemId);

    /// <summary>
    /// Deletes every item thumbnail of the library, including those of items no longer in it, for "Regenerate all
    /// thumbnails" (E02-S06-T02). The keyframe pictures stay, since only the analysis makes them. A file that can't be
    /// deleted, e.g. one open elsewhere, is logged and left; regenerating replaces it.
    /// </summary>
    /// <param name="library">The library.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of files deleted.</returns>
    int Clear(LibraryLayout library, CancellationToken cancellationToken = default);
}
