using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using SkiaSharp;

namespace MediaFileClassifier.Infrastructure.Media.Thumbnails;

/// <summary>
/// Decodes pictures with SkiaSharp, which Avalonia already ships on every platform, in sRGB with premultiplied alpha.
/// </summary>
internal static class SkiaDecoding
{
    /// <summary>
    /// Reads a file for the decoder, which needs to seek: straight from disk when it has a local path, otherwise into
    /// memory, since content streams, as on Android, may not seek.
    /// </summary>
    /// <param name="content">The file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The file's bytes.</returns>
    public static async Task<SKData> ReadAsync(MediaContent content, CancellationToken cancellationToken)
    {
        if (content.LocalPath is { } path)
        {
            return SKData.Create(path) ?? throw new InvalidDataException("The file can't be read.");
        }

        var stream = await content.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            buffer.Position = 0;
            return SKData.Create(buffer);
        }
    }

    /// <summary>
    /// Opens a decoder.
    /// </summary>
    /// <param name="data">The file's bytes.</param>
    /// <returns>The decoder.</returns>
    /// <exception cref="InvalidDataException">The content isn't a picture SkiaSharp can read.</exception>
    public static SKCodec OpenCodec(SKData data)
        => SKCodec.Create(data) ?? throw new InvalidDataException("The picture can't be decoded.");

    /// <summary>
    /// Gets the decode target of a picture: sRGB, premultiplied, at the given size.
    /// </summary>
    /// <param name="size">The size.</param>
    /// <returns>The target.</returns>
    public static SKImageInfo TargetInfo(SKSizeI size)
        => new(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());

    /// <summary>
    /// Decodes the first frame. Where the format can decode at a reduced size, as JPEG can in eighths, the size closest
    /// to <paramref name="preferredLongestEdge"/> is used if its longest edge still reaches
    /// <paramref name="minLongestEdge"/>, which is much faster for large photos.
    /// </summary>
    /// <param name="codec">The decoder.</param>
    /// <param name="preferredLongestEdge">The longest edge wanted.</param>
    /// <param name="minLongestEdge">The smallest longest edge that will do.</param>
    /// <returns>The picture as stored, not yet upright.</returns>
    public static SKBitmap DecodeStill(SKCodec codec, int preferredLongestEdge, int minLongestEdge)
    {
        var size = codec.Info.Size;
        var longest = Math.Max(size.Width, size.Height);
        if (longest > preferredLongestEdge)
        {
            var scaled = codec.GetScaledDimensions((float)preferredLongestEdge / longest);
            if (Math.Max(scaled.Width, scaled.Height) >= minLongestEdge)
            {
                size = scaled;
            }
        }

        var info = TargetInfo(size);
        var bitmap = new SKBitmap(info);
        try
        {
            Check(codec.GetPixels(info, bitmap.GetPixels()));
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Throws unless a decode produced pixels. A truncated file decodes as far as it goes, like in a browser.
    /// </summary>
    /// <param name="result">The decoder's result.</param>
    /// <exception cref="InvalidDataException">Nothing was decoded.</exception>
    public static void Check(SKCodecResult result)
    {
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput or SKCodecResult.ErrorInInput))
        {
            throw new InvalidDataException($"The picture can't be decoded: {result}.");
        }
    }
}
