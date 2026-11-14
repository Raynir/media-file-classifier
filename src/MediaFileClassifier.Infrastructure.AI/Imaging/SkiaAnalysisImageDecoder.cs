using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.AI.Embeddings;
using SkiaSharp;

namespace MediaFileClassifier.Infrastructure.AI.Imaging;

/// <summary>
/// Decodes with SkiaSharp the way the SigLIP 2 preprocessing does (D244), but at the size the item's analyzers need
/// together, and shrinks with high-quality sampling.
/// </summary>
internal sealed class SkiaAnalysisImageDecoder : IAnalysisImageDecoder
{
    /// <inheritdoc />
    public DecodedImage Decode(ReadOnlyMemory<byte> encoded, int minShortSide, int minLongestEdge, string? name = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minShortSide);
        ArgumentOutOfRangeException.ThrowIfNegative(minLongestEdge);

        using var data = SKData.CreateCopy(encoded.Span);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException($"The picture {name} can't be decoded.");
        var size = DecodeSize(codec, minShortSide, minLongestEdge);
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var result = codec.GetPixels(info, out var pixels);
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput or SKCodecResult.ErrorInInput))
        {
            throw new InvalidDataException($"The picture {name} can't be decoded: {result}.");
        }

        Siglip2ImagePreprocessor.CompositeOverWhite(pixels);
        var (upright, width, height) = Siglip2ImagePreprocessor.Orient(pixels, info.Width, info.Height, codec.EncodedOrigin);
        return new DecodedImage(upright, width, height);
    }

    /// <inheritdoc />
    public DecodedImage Shrink(DecodedImage image, int maxLongestEdge)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLongestEdge);
        if (image.LongestEdge <= maxLongestEdge)
        {
            return image;
        }

        var scale = (double)maxLongestEdge / image.LongestEdge;
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));
        var sourceInfo = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var source = new SKBitmap();
        var pixels = image.Pixels.ToArray();
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            source.InstallPixels(sourceInfo, handle.AddrOfPinnedObject(), sourceInfo.RowBytes);
            using var target = source.Resize(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul), new SKSamplingOptions(SKCubicResampler.Mitchell))
                ?? throw new InvalidOperationException("The picture couldn't be shrunk.");
            return new DecodedImage(target.Bytes, width, height);
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>
    /// Picks the smallest size the decoder can produce directly whose sides, upright, still meet both limits.
    /// </summary>
    /// <param name="codec">The codec.</param>
    /// <param name="minShortSide">The smallest short side.</param>
    /// <param name="minLongestEdge">The smallest longest edge.</param>
    /// <returns>The size to decode at.</returns>
    internal static SKSizeI DecodeSize(SKCodec codec, int minShortSide, int minLongestEdge)
    {
        var size = codec.Info.Size;
        foreach (var scale in (ReadOnlySpan<float>)[0.125f, 0.25f, 0.5f])
        {
            var scaled = codec.GetScaledDimensions(scale);
            if (scaled != size
                && Math.Min(scaled.Width, scaled.Height) >= minShortSide
                && Math.Max(scaled.Width, scaled.Height) >= minLongestEdge)
            {
                return scaled;
            }
        }

        return size;
    }
}
