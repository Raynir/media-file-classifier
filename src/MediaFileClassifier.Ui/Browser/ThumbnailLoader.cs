using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;

namespace MediaFileClassifier.Ui.Browser;

/// <summary>
/// Finds an item's thumbnail file in the open library's thumbnail cache (D127) and decodes it through the shared
/// <see cref="ThumbnailBitmapCache"/> (E14-S01-T03). The file check runs off the UI thread.
/// </summary>
public sealed class ThumbnailLoader
{
    private readonly ILibrarySession session;
    private readonly IThumbnailCache thumbnails;
    private readonly ThumbnailBitmapCache bitmaps;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThumbnailLoader"/> class.
    /// </summary>
    /// <param name="session">Holds the open library.</param>
    /// <param name="thumbnails">The thumbnail files.</param>
    /// <param name="bitmaps">The decoded thumbnails.</param>
    public ThumbnailLoader(ILibrarySession session, IThumbnailCache thumbnails, ThumbnailBitmapCache bitmaps)
    {
        this.session = session;
        this.thumbnails = thumbnails;
        this.bitmaps = bitmaps;
    }

    /// <summary>
    /// Picks the stored thumbnail size for a tile (E03-S04): the large one once the image is wider than the small one
    /// on screen.
    /// </summary>
    /// <param name="tileWidth">Tile width in device-independent pixels.</param>
    /// <param name="renderScaling">The screen's scaling, e.g. 1.5 at 150%.</param>
    /// <returns>The size to load.</returns>
    public static ThumbnailSize SizeFor(double tileWidth, double renderScaling) =>
        tileWidth * renderScaling > (int)ThumbnailSize.Small ? ThumbnailSize.Large : ThumbnailSize.Small;

    /// <summary>
    /// Loads an item's thumbnail, falling back to the other size when the preferred one isn't there.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="size">The preferred size.</param>
    /// <param name="cancellationToken">Cancellation token, e.g. when the tile scrolled away.</param>
    /// <returns>A lease on the bitmap, or <see langword="null"/> when the item has no thumbnail yet.</returns>
    public async Task<ThumbnailLease?> LoadAsync(MediaItemSummary item, ThumbnailSize size, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (session.Current?.Layout is not { } layout)
        {
            return null;
        }

        var other = size == ThumbnailSize.Large ? ThumbnailSize.Small : ThumbnailSize.Large;
        var found = await Task.Run(
            () =>
            {
                try
                {
                    var path = thumbnails.Find(layout, item.Id, size, item.FileModifiedAt) ?? thumbnails.Find(layout, item.Id, other, item.FileModifiedAt);
                    return path is null ? default : (Path: path, Version: File.GetLastWriteTimeUtc(path).Ticks);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // E.g. the library's drive went away: the tile shows its placeholder.
                    return default;
                }
            },
            cancellationToken).ConfigureAwait(false);
        return found.Path is null ? null : await bitmaps.LoadAsync(found.Path, found.Version, cancellationToken).ConfigureAwait(false);
    }
}
