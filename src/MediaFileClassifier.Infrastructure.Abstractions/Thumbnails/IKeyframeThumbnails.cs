using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Frames;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;

namespace MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;

/// <summary>
/// The cached pictures of a video's or animated image's keyframes, for the preview's keyframe strip (E14-S13, D227).
/// They are WebP files next to the item's thumbnails (<see cref="LibraryLayout.GetKeyframeThumbnailPath"/>) with no
/// database column. Analysis writes them from the frames it has already decoded; deleting an item's thumbnails
/// (<see cref="IThumbnailCache.Invalidate"/>) deletes them too.
/// </summary>
public interface IKeyframeThumbnails
{
    /// <summary>
    /// The longest edge of a stored keyframe picture: twice the strip's 96 px, so it stays sharp at 200% scaling.
    /// </summary>
    public const int LongestEdge = 192;

    /// <summary>
    /// Replaces an item's keyframe pictures with those of freshly sampled frames. Never throws for a file-system
    /// problem: the strip then shows timestamps without pictures.
    /// </summary>
    /// <param name="library">The library's folder layout.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="frames">The sampled frames, as stored with their keyframes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the pictures are written.</returns>
    Task StoreAsync(LibraryLayout library, Id<MediaItem> itemId, IReadOnlyList<SampledFrame> frames, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a keyframe's picture.
    /// </summary>
    /// <param name="library">The library's folder layout.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="index">The keyframe's index among the item's keyframes.</param>
    /// <returns>The file's full path, or <see langword="null"/> when there isn't one (not analysed since this existed).</returns>
    string? Find(LibraryLayout library, Id<MediaItem> itemId, int index);
}
