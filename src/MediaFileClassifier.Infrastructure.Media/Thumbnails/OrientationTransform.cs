using MediaFileClassifier.Domain.Metadata;
using SkiaSharp;

namespace MediaFileClassifier.Infrastructure.Media.Thumbnails;

/// <summary>
/// Turns stored pixels upright using the orientation from the item's metadata (E03-S03), so thumbnails never appear
/// sideways. The decoders leave the pixels as stored.
/// </summary>
internal static class OrientationTransform
{
    /// <summary>
    /// Applies an orientation. Takes ownership of <paramref name="source"/>: it is returned when nothing changes, and
    /// disposed otherwise.
    /// </summary>
    /// <param name="source">The stored pixels.</param>
    /// <param name="orientation">The orientation.</param>
    /// <returns>The upright picture.</returns>
    public static SKBitmap Apply(SKBitmap source, ImageOrientation orientation)
    {
        if (orientation is ImageOrientation.Normal || !Enum.IsDefined(orientation))
        {
            return source;
        }

        var swapsAxes = orientation >= ImageOrientation.Transpose;
        var result = new SKBitmap(source.Info.WithSize(swapsAxes ? source.Height : source.Width, swapsAxes ? source.Width : source.Height));
        using (var canvas = new SKCanvas(result))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.SetMatrix(ToMatrix(orientation, source.Width, source.Height));
            canvas.DrawBitmap(source, 0, 0);
        }

        source.Dispose();
        return result;
    }

    /// <summary>
    /// Maps a stored pixel (x, y) of a <paramref name="width"/> × <paramref name="height"/> picture to its upright
    /// position: x' = ScaleX·x + SkewX·y + TransX, y' = SkewY·x + ScaleY·y + TransY.
    /// </summary>
    internal static SKMatrix ToMatrix(ImageOrientation orientation, int width, int height) => orientation switch
    {
        ImageOrientation.FlipHorizontal => new(-1, 0, width, 0, 1, 0, 0, 0, 1),
        ImageOrientation.Rotate180 => new(-1, 0, width, 0, -1, height, 0, 0, 1),
        ImageOrientation.FlipVertical => new(1, 0, 0, 0, -1, height, 0, 0, 1),
        ImageOrientation.Transpose => new(0, 1, 0, 1, 0, 0, 0, 0, 1),
        ImageOrientation.Rotate90 => new(0, -1, height, 1, 0, 0, 0, 0, 1),
        ImageOrientation.Transverse => new(0, -1, height, -1, 0, width, 0, 0, 1),
        ImageOrientation.Rotate270 => new(0, 1, 0, -1, 0, width, 0, 0, 1),
        _ => SKMatrix.Identity,
    };
}
