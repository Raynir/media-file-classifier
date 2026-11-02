using MediaFileClassifier.Infrastructure.Abstractions.Hashing;
using SkiaSharp;

namespace MediaFileClassifier.Infrastructure.Hashing;

/// <summary>
/// Computes a DCT perceptual hash (pHash) with SkiaSharp decoding, which works the same on desktop and Android.
/// </summary>
/// <remarks>
/// The image is decoded (JPEGs at a reduced scale), composited over white, converted to luma and box-filtered to
/// 32×32. The 8×8 lowest frequencies of its DCT, without the DC term, are compared to their median: each one above
/// it is a 1 bit, in row-major order from the most significant bit. Resampling is done here rather than by Skia, so
/// the hash doesn't depend on Skia's filtering.
/// </remarks>
public sealed class SkiaPerceptualHasher : IPerceptualHasher
{
    private const int SampleSize = 32;
    private const int HashSize = 8;

    /// <summary>
    /// The shorter side JPEGs are decoded at, at least. Scaled decoding is much faster, and box filtering from
    /// this size down to <see cref="SampleSize"/> still averages out compression noise.
    /// </summary>
    private const int MinDecodeSide = 128;

    /// <summary>
    /// DCT-II basis rows 1 to 8, for a 32-sample signal. The DC row is left out, and so is the normalization,
    /// because only the order of the coefficients matters.
    /// </summary>
    private static readonly double[,] DctBasis = CreateDctBasis();

    /// <inheritdoc />
    public ulong? Compute(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var luma = Decode(content);
        return luma is null ? null : HashLuma(luma);
    }

    /// <summary>
    /// Hashes a <see cref="SampleSize"/>×<see cref="SampleSize"/> luma grid, row-major.
    /// </summary>
    /// <param name="luma">The grid.</param>
    /// <returns>The hash.</returns>
    private static ulong HashLuma(double[] luma)
    {
        // F = B · P · Bᵀ, with B the 8×32 basis and P the 32×32 grid.
        var rows = new double[HashSize * SampleSize];
        for (var k = 0; k < HashSize; k++)
        {
            for (var x = 0; x < SampleSize; x++)
            {
                double sum = 0;
                for (var y = 0; y < SampleSize; y++)
                {
                    sum += DctBasis[k, y] * luma[(y * SampleSize) + x];
                }

                rows[(k * SampleSize) + x] = sum;
            }
        }

        var coefficients = new double[HashSize * HashSize];
        for (var k = 0; k < HashSize; k++)
        {
            for (var l = 0; l < HashSize; l++)
            {
                double sum = 0;
                for (var x = 0; x < SampleSize; x++)
                {
                    sum += rows[(k * SampleSize) + x] * DctBasis[l, x];
                }

                coefficients[(k * HashSize) + l] = sum;
            }
        }

        var sorted = coefficients.Order().ToArray();
        var median = (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2;
        ulong hash = 0;
        foreach (var coefficient in coefficients)
        {
            hash = (hash << 1) | (coefficient > median ? 1UL : 0UL);
        }

        return hash;
    }

    private static double[]? Decode(Stream content)
    {
        var seekable = content.CanSeek ? content : CopyToMemory(content);
        using var codec = SKCodec.Create(new SKManagedStream(seekable, disposeManagedStream: !ReferenceEquals(seekable, content)));
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
        {
            return null;
        }

        var shorterSide = Math.Min(codec.Info.Width, codec.Info.Height);
        var size = shorterSide > MinDecodeSide ? codec.GetScaledDimensions((float)MinDecodeSide / shorterSide) : codec.Info.Size;
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);

        // The default options decode the first frame of an animated image.
        if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success)
        {
            return null;
        }

        return Orient(Downsample(bitmap.GetPixelSpan(), size.Width, size.Height), codec.EncodedOrigin);
    }

    private static MemoryStream CopyToMemory(Stream content)
    {
        var memory = new MemoryStream();
        content.CopyTo(memory);
        memory.Position = 0;
        return memory;
    }

    /// <summary>
    /// Converts RGBA pixels to luma over white and box-filters them to the sample grid.
    /// </summary>
    private static double[] Downsample(ReadOnlySpan<byte> rgba, int width, int height)
    {
        var grid = new double[SampleSize * SampleSize];
        for (var gy = 0; gy < SampleSize; gy++)
        {
            var y0 = gy * height / SampleSize;
            var y1 = Math.Max((gy + 1) * height / SampleSize, y0 + 1);
            for (var gx = 0; gx < SampleSize; gx++)
            {
                var x0 = gx * width / SampleSize;
                var x1 = Math.Max((gx + 1) * width / SampleSize, x0 + 1);
                double sum = 0;
                for (var y = y0; y < y1; y++)
                {
                    for (var x = x0; x < x1; x++)
                    {
                        var pixel = rgba.Slice(((y * width) + x) * 4, 4);
                        var alpha = pixel[3] / 255.0;
                        var luma = (0.299 * pixel[0]) + (0.587 * pixel[1]) + (0.114 * pixel[2]);
                        sum += (luma * alpha) + (255 * (1 - alpha));
                    }
                }

                grid[(gy * SampleSize) + gx] = sum / ((y1 - y0) * (x1 - x0));
            }
        }

        return grid;
    }

    /// <summary>
    /// Turns the stored grid to the displayed orientation. Rotating the square grid gives the same result as
    /// rotating the image before scaling it.
    /// </summary>
    private static double[] Orient(double[] stored, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft)
        {
            return stored;
        }

        const int last = SampleSize - 1;
        var displayed = new double[stored.Length];
        for (var y = 0; y < SampleSize; y++)
        {
            for (var x = 0; x < SampleSize; x++)
            {
                var (sx, sy) = origin switch
                {
                    SKEncodedOrigin.TopRight => (last - x, y),
                    SKEncodedOrigin.BottomRight => (last - x, last - y),
                    SKEncodedOrigin.BottomLeft => (x, last - y),
                    SKEncodedOrigin.LeftTop => (y, x),
                    SKEncodedOrigin.RightTop => (y, last - x),
                    SKEncodedOrigin.RightBottom => (last - y, last - x),
                    SKEncodedOrigin.LeftBottom => (last - y, x),
                    _ => (x, y),
                };
                displayed[(y * SampleSize) + x] = stored[(sy * SampleSize) + sx];
            }
        }

        return displayed;
    }

    private static double[,] CreateDctBasis()
    {
        var basis = new double[HashSize, SampleSize];
        for (var k = 0; k < HashSize; k++)
        {
            for (var n = 0; n < SampleSize; n++)
            {
                basis[k, n] = Math.Cos(Math.PI * (k + 1) * ((2 * n) + 1) / (2.0 * SampleSize));
            }
        }

        return basis;
    }
}
