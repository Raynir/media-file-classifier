using Avalonia;
using Avalonia.Labs.AnimatedImage;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Previews;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.UseCases.Browsing.ItemDetails;
using MediaFileClassifier.UseCases.Browsing.ItemLocation;
using SkiaSharp;

namespace MediaFileClassifier.Ui.Details;

/// <summary>
/// A bitmap shown in the details panel preview, and what it is.
/// </summary>
public sealed class PreviewImage : IDisposable
{
    private Action? release;

    /// <summary>
    /// Initializes a new instance of the <see cref="PreviewImage"/> class.
    /// </summary>
    /// <param name="bitmap">The bitmap.</param>
    /// <param name="size">The bitmap's size in pixels.</param>
    /// <param name="sourceSize">The stored image's size in pixels, which the bitmap may be scaled down from.</param>
    /// <param name="orientation">How the bitmap must be turned to show it upright.</param>
    /// <param name="kind">Where the bitmap comes from.</param>
    /// <param name="release">Releases the bitmap.</param>
    public PreviewImage(Bitmap bitmap, PixelSize size, PixelSize sourceSize, ImageOrientation orientation, PreviewKind kind, Action release)
    {
        Bitmap = bitmap;
        Size = size;
        SourceSize = sourceSize;
        Orientation = orientation;
        Kind = kind;
        this.release = release;
    }

    /// <summary>
    /// Gets the bitmap.
    /// </summary>
    public Bitmap Bitmap { get; }

    /// <summary>
    /// Gets the bitmap's size in pixels, as decoded.
    /// </summary>
    public PixelSize Size { get; }

    /// <summary>
    /// Gets the stored image's size in pixels (orientation not applied), which the bitmap may be scaled down from.
    /// </summary>
    public PixelSize SourceSize { get; }

    /// <summary>
    /// Gets how the bitmap must be turned to show it upright. Thumbnails are already upright.
    /// </summary>
    public ImageOrientation Orientation { get; }

    /// <summary>
    /// Gets where the bitmap comes from.
    /// </summary>
    public PreviewKind Kind { get; }

    /// <inheritdoc />
    public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
}

/// <summary>
/// Where a preview bitmap comes from, from the least to the most detailed.
/// </summary>
public enum PreviewKind
{
    /// <summary>The item's large thumbnail.</summary>
    Thumbnail,

    /// <summary>The original file, decoded at about the screen's size.</summary>
    ScreenSized,

    /// <summary>The original file at full resolution, for zooming in.</summary>
    FullResolution,
}

/// <summary>
/// An animated image the preview can play, or why it can't.
/// </summary>
/// <param name="Animation">The loaded animation, which the caller owns; <see langword="null"/> when it is not played.</param>
/// <param name="DecodedBytes">What decoding every frame would take, when that is over the limit.</param>
public sealed record AnimatedImageLoad(IAnimatedBitmap? Animation, long DecodedBytes = 0)
{
    /// <summary>
    /// Gets a value indicating whether the animation is too large to play, so the preview shows its first frame (D070).
    /// </summary>
    public bool IsTooLarge => Animation is null && DecodedBytes > 0;
}

/// <summary>
/// Loads the details panel preview (E14-S05-T02): the large thumbnail at once, then the original image decoded at
/// about the screen's size, and at full resolution only when zoomed in past that. Videos, and files that are missing
/// or in an offline folder, keep the thumbnail (D025).
/// </summary>
/// <param name="thumbnails">Loads thumbnails.</param>
/// <param name="files">Reads the original files.</param>
/// <param name="decoder">Decodes the original images.</param>
public sealed class PreviewLoader(ThumbnailLoader thumbnails, IMediaFileSource files, IPreviewDecoder decoder)
{
    /// <summary>
    /// The most memory an animated image may take once every frame is decoded, 256 MB. The animation control keeps all
    /// frames as bitmaps; above this the preview shows the first frame and offers Open in default app (D070, D026).
    /// </summary>
    public const long MaxAnimationBytes = 256L * 1024 * 1024;

    /// <summary>
    /// Gets what decoding every frame of an image takes: its width and height in pixels, 4 bytes each, for every frame.
    /// </summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="frames">The number of frames.</param>
    /// <returns>The size in bytes.</returns>
    public static long DecodedAnimationBytes(int width, int height, int frames) => (long)width * height * 4 * frames;

    /// <summary>
    /// Gets whether an item's original file can be shown, rather than its thumbnail.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns><see langword="true"/> for images whose file is available.</returns>
    public static bool CanShowOriginal(ItemDetails item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.MediaType != MediaType.Video && item.Location.Availability == ItemAvailability.Available;
    }

    /// <summary>
    /// Loads the item's large thumbnail.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The thumbnail, or <see langword="null"/> when there's none yet.</returns>
    public async Task<PreviewImage?> LoadThumbnailAsync(ItemDetails item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var lease = await thumbnails.LoadAsync(item.Id, item.FileModifiedAt, ThumbnailSize.Large, cancellationToken).ConfigureAwait(false);
        return lease is null ? null : new PreviewImage(lease.Bitmap, lease.Bitmap.PixelSize, lease.Bitmap.PixelSize, ImageOrientation.Normal, PreviewKind.Thumbnail, lease.Dispose);
    }

    /// <summary>
    /// Decodes the item's original file.
    /// </summary>
    /// <param name="item">The item; see <see cref="CanShowOriginal"/>.</param>
    /// <param name="maxLongestEdge">The decoded image's longer side at most, or <see langword="null"/> for full resolution.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="IOException">The file can't be read.</exception>
    /// <exception cref="InvalidDataException">The file can't be decoded.</exception>
    public async Task<PreviewImage> LoadOriginalAsync(ItemDetails item, int? maxLongestEdge, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var location = item.Location.Location;
        var content = new MediaContent(item.Id, item.FileModifiedAt, token => files.OpenReadAsync(location, token))
        {
            LocalPath = item.Location.FullPath,
        };
        using var decoded = await decoder.DecodeAsync(content, maxLongestEdge, cancellationToken).ConfigureAwait(false);

        // The bitmap copies the pixels, so the decoded ones can go at once.
        var size = new PixelSize(decoded.Width, decoded.Height);
        var bitmap = await Task.Run(
            () => new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, decoded.Pixels, size, new Vector(96, 96), decoded.RowBytes),
            cancellationToken).ConfigureAwait(false);
        var kind = decoded.IsFullResolution ? PreviewKind.FullResolution : PreviewKind.ScreenSized;
        return new PreviewImage(bitmap, size, new PixelSize(decoded.SourceWidth, decoded.SourceHeight), item.Technical?.Orientation ?? ImageOrientation.Normal, kind, bitmap.Dispose);
    }

    /// <summary>
    /// Loads an animated GIF or WebP for playing: reads its size and frame count first, and only decodes every frame
    /// when they fit <see cref="MaxAnimationBytes"/>.
    /// </summary>
    /// <param name="item">The item; see <see cref="CanShowOriginal"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The animation, or its decoded size when it is too large. <see langword="null"/> when the file has one frame only.</returns>
    /// <exception cref="IOException">The file can't be read.</exception>
    /// <exception cref="InvalidDataException">The file can't be decoded.</exception>
    public async Task<AnimatedImageLoad?> LoadAnimationAsync(ItemDetails item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var location = item.Location.Location;
        int width, height, frames;
        await using (var probe = await files.OpenReadAsync(location, cancellationToken).ConfigureAwait(false))
        {
            // SKCodec needs a seekable stream; reading the header is cheap.
            var seekable = probe;
            if (!seekable.CanSeek)
            {
                var copy = new MemoryStream();
                await probe.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
                copy.Position = 0;
                seekable = copy;
            }

            using var codec = SKCodec.Create(seekable) ?? throw new InvalidDataException("The image can't be decoded.");
            width = codec.Info.Width;
            height = codec.Info.Height;
            frames = codec.FrameCount;
        }

        if (frames <= 1)
        {
            return null;
        }

        var bytes = DecodedAnimationBytes(width, height, frames);
        if (bytes > MaxAnimationBytes)
        {
            return new AnimatedImageLoad(null, bytes);
        }

        var stream = await files.OpenReadAsync(location, cancellationToken).ConfigureAwait(false);
        var animation = IAnimatedBitmap.Load(stream, disposeStream: true);
        try
        {
            await WaitForInitializedAsync(animation, cancellationToken).ConfigureAwait(false);
            return new AnimatedImageLoad(animation);
        }
        catch
        {
            (animation as IDisposable)?.Dispose();
            throw;
        }
    }

    private static async Task WaitForInitializedAsync(IAnimatedBitmap animation, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnInitialized(object? sender, EventArgs e) => done.TrySetResult();
        void OnFailed(object? sender, AnimatedBitmapFailedEventArgs e) => done.TrySetException(new InvalidDataException("The animation can't be decoded.", e.Exception));
        animation.Initialized += OnInitialized;
        animation.Failed += OnFailed;
        try
        {
            if (animation.IsFailed)
            {
                throw new InvalidDataException("The animation can't be decoded.");
            }

            if (!animation.IsInitialized)
            {
                // Loading only opens the file; the frames are decoded once the animation is started.
                animation.Init();
                await done.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            animation.Initialized -= OnInitialized;
            animation.Failed -= OnFailed;
        }
    }
}
