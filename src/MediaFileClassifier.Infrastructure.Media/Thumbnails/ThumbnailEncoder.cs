using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using SkiaSharp;

namespace MediaFileClassifier.Infrastructure.Media.Thumbnails;

/// <summary>
/// Scales an upright picture to every <see cref="ThumbnailSize"/> and encodes it as WebP at quality 80, which was
/// smaller than JPEG at the same quality and keeps transparency (E03-S04-T01).
/// </summary>
internal static class ThumbnailEncoder
{
    /// <summary>
    /// The WebP quality.
    /// </summary>
    public const int Quality = 80;

    private static readonly SKSamplingOptions Halving = new(SKFilterMode.Linear);
    private static readonly SKSamplingOptions FinalStep = new(SKCubicResampler.Mitchell);

    /// <summary>
    /// Gets the longest edge a source picture should have: twice the largest size, so the last downscale step is at
    /// most 2:1. Anything from the largest size up will do.
    /// </summary>
    public static int SourceLongestEdge => 2 * (int)ThumbnailSize.Large;

    /// <summary>
    /// Encodes the thumbnails of a picture.
    /// </summary>
    /// <param name="source">The upright picture.</param>
    /// <returns>The encoded image of each size.</returns>
    public static IReadOnlyDictionary<ThumbnailSize, byte[]> Encode(SKBitmap source)
    {
        var images = new Dictionary<ThumbnailSize, byte[]>();
        foreach (var size in Enum.GetValues<ThumbnailSize>())
        {
            using var scaled = ScaleDown(source, (int)size);
            using var data = (scaled ?? source).Encode(SKEncodedImageFormat.Webp, Quality)
                ?? throw new InvalidOperationException("The thumbnail couldn't be encoded.");
            images[size] = data.ToArray();
        }

        return images;
    }

    /// <summary>
    /// Scales a picture down to a longest edge. Halves with a linear filter, a 2×2 box average, while the picture is
    /// more than twice the target, so large reductions don't alias, then takes the last step with a Mitchell cubic.
    /// </summary>
    /// <returns>The scaled picture, or <see langword="null"/> when the picture already fits.</returns>
    internal static SKBitmap? ScaleDown(SKBitmap source, int longestEdge)
    {
        if (Math.Max(source.Width, source.Height) <= longestEdge)
        {
            return null;
        }

        var current = source;
        while (Math.Max(current.Width, current.Height) > 2 * longestEdge)
        {
            var half = Resize(current, Math.Max(1, current.Width / 2), Math.Max(1, current.Height / 2), Halving);
            if (!ReferenceEquals(current, source))
            {
                current.Dispose();
            }

            current = half;
        }

        var scale = (double)longestEdge / Math.Max(current.Width, current.Height);
        var result = Resize(current, Math.Max(1, (int)Math.Round(current.Width * scale)), Math.Max(1, (int)Math.Round(current.Height * scale)), FinalStep);
        if (!ReferenceEquals(current, source))
        {
            current.Dispose();
        }

        return result;
    }

    private static SKBitmap Resize(SKBitmap source, int width, int height, SKSamplingOptions sampling)
        => source.Resize(source.Info.WithSize(width, height), sampling)
            ?? throw new InvalidOperationException("The picture couldn't be scaled.");
}
