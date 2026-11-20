using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Ui.Browser;

namespace MediaFileClassifier.Ui.Details;

/// <summary>
/// Finds a keyframe's cached picture in the open library (E14-S13, D227) and decodes it through the shared
/// <see cref="ThumbnailBitmapCache"/>. The file check runs off the UI thread.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="pictures">The keyframe picture files.</param>
/// <param name="bitmaps">The decoded pictures.</param>
public sealed class KeyframePictureLoader(ILibrarySession session, IKeyframeThumbnails pictures, ThumbnailBitmapCache bitmaps)
{
    /// <summary>
    /// Loads a keyframe's picture.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="index">The keyframe's index among the item's keyframes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A lease on the bitmap, or <see langword="null"/> when there is no picture (the item wasn't analysed since pictures were stored).</returns>
    public async Task<ThumbnailLease?> LoadAsync(Id<MediaItem> itemId, int index, CancellationToken cancellationToken)
    {
        if (session.Current?.Layout is not { } layout)
        {
            return null;
        }

        var found = await Task.Run(
            () =>
            {
                try
                {
                    var path = pictures.Find(layout, itemId, index);
                    return path is null ? default : (Path: path, Version: File.GetLastWriteTimeUtc(path).Ticks);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return default;
                }
            },
            cancellationToken).ConfigureAwait(false);
        return found.Path is null ? null : await bitmaps.LoadAsync(found.Path, found.Version, cancellationToken).ConfigureAwait(false);
    }
}
