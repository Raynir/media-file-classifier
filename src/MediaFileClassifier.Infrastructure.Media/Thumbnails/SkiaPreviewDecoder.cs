using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Previews;
using SkiaSharp;

namespace MediaFileClassifier.Infrastructure.Media.Thumbnails;

/// <summary>
/// Decodes preview images with Skia (E14-S05-T02). A JPEG is decoded straight at a reduced scale, which is what
/// makes a 50-megapixel photo quick to preview; other formats are decoded whole and then scaled down.
/// </summary>
public sealed class SkiaPreviewDecoder : IPreviewDecoder
{
    /// <inheritdoc />
    public async Task<DecodedImage> DecodeAsync(MediaContent content, int? maxLongestEdge, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var data = await SkiaDecoding.ReadAsync(content, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return await Task.Run(() => Decode(data, maxLongestEdge), cancellationToken).ConfigureAwait(false);
    }

    private static DecodedImage Decode(SKData data, int? maxLongestEdge)
    {
        using var codec = SkiaDecoding.OpenCodec(data);
        var size = codec.Info.Size;
        var longest = Math.Max(size.Width, size.Height);
        var limit = maxLongestEdge is { } edge && edge < longest ? edge : longest;
        var target = limit == longest
            ? size
            : new SKSizeI(Math.Max(1, (int)Math.Round((double)size.Width * limit / longest)), Math.Max(1, (int)Math.Round((double)size.Height * limit / longest)));

        // The codec's own scaling (JPEG: 1/2, 1/4, 1/8) to the smallest size still at least the target.
        var decodeSize = size;
        if (target != size)
        {
            var scaled = codec.GetScaledDimensions((float)limit / longest);
            if (scaled.Width >= target.Width && scaled.Height >= target.Height)
            {
                decodeSize = scaled;
            }
        }

        var decoded = new SKBitmap(Info(decodeSize));
        try
        {
            SkiaDecoding.Check(codec.GetPixels(decoded.Info, decoded.GetPixels()));
            if (decodeSize != target)
            {
                var resized = decoded.Resize(Info(target), new SKSamplingOptions(SKCubicResampler.Mitchell))
                    ?? throw new InvalidDataException("The picture can't be scaled.");
                decoded.Dispose();
                decoded = resized;
            }

            return new DecodedImage(decoded.Width, decoded.Height, decoded.RowBytes, decoded.GetPixels(), size.Width, size.Height, decoded);
        }
        catch
        {
            decoded.Dispose();
            throw;
        }
    }

    private static SKImageInfo Info(SKSizeI size) =>
        new(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
}
