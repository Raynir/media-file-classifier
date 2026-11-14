namespace MediaFileClassifier.Infrastructure.AI.Embeddings;

/// <summary>
/// Resizes 8-bit RGB pixels exactly as Pillow's <c>Image.resize(size, BILINEAR)</c> does, which is the resize the
/// SigLIP 2 processor uses (D092). Pillow widens the triangle filter by the scale factor when it shrinks, so it
/// antialiases; a GPU-style bilinear sample doesn't, and gives the model different pixels.
/// </summary>
/// <remarks>
/// A port of <c>libImaging/Resample.c</c>: separable passes, horizontal first, with 22-bit fixed-point weights and
/// 8-bit rounding between the passes. For losslessly decoded input the output is byte-identical to Pillow.
/// </remarks>
internal static class PilBilinearResampler
{
    private const int PrecisionBits = 32 - 8 - 2;

    /// <summary>
    /// Resizes RGBA pixels to RGB pixels. Alpha is ignored: the caller has composited it already.
    /// </summary>
    /// <param name="rgba">Source RGBA8888 pixels, stride <paramref name="width"/> * 4.</param>
    /// <param name="width">Source width.</param>
    /// <param name="height">Source height.</param>
    /// <param name="targetWidth">Target width.</param>
    /// <param name="targetHeight">Target height.</param>
    /// <returns>RGB888 pixels, stride <paramref name="targetWidth"/> * 3.</returns>
    public static byte[] ResizeToRgb(ReadOnlySpan<byte> rgba, int width, int height, int targetWidth, int targetHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetHeight);
        if (rgba.Length < (long)width * height * 4)
        {
            throw new ArgumentException("The pixel buffer is smaller than width * height * 4.", nameof(rgba));
        }

        var rgb = new byte[width * height * 3];
        for (int source = 0, target = 0; target < rgb.Length; source += 4, target += 3)
        {
            rgb[target] = rgba[source];
            rgb[target + 1] = rgba[source + 1];
            rgb[target + 2] = rgba[source + 2];
        }

        var horizontal = width == targetWidth ? rgb : ResampleHorizontal(rgb, width, height, targetWidth);
        return height == targetHeight ? horizontal : ResampleVertical(horizontal, targetWidth, height, targetHeight);
    }

    private static byte[] ResampleHorizontal(byte[] source, int width, int height, int targetWidth)
    {
        var (bounds, weights, kernelSize) = PrecomputeWeights(width, targetWidth);
        var target = new byte[targetWidth * height * 3];
        for (var y = 0; y < height; y++)
        {
            var row = y * width * 3;
            var outRow = y * targetWidth * 3;
            for (var x = 0; x < targetWidth; x++)
            {
                var start = bounds[x * 2];
                var count = bounds[(x * 2) + 1];
                var k = x * kernelSize;
                int r = 1 << (PrecisionBits - 1), g = r, b = r;
                for (var i = 0; i < count; i++)
                {
                    var pixel = row + ((start + i) * 3);
                    var weight = weights[k + i];
                    r += source[pixel] * weight;
                    g += source[pixel + 1] * weight;
                    b += source[pixel + 2] * weight;
                }

                var output = outRow + (x * 3);
                target[output] = Clip8(r);
                target[output + 1] = Clip8(g);
                target[output + 2] = Clip8(b);
            }
        }

        return target;
    }

    private static byte[] ResampleVertical(byte[] source, int width, int height, int targetHeight)
    {
        var (bounds, weights, kernelSize) = PrecomputeWeights(height, targetHeight);
        var stride = width * 3;
        var target = new byte[stride * targetHeight];
        var sums = new int[stride];
        for (var y = 0; y < targetHeight; y++)
        {
            var start = bounds[y * 2];
            var count = bounds[(y * 2) + 1];
            var k = y * kernelSize;
            Array.Fill(sums, 1 << (PrecisionBits - 1));

            // Row by row, so the source is read in memory order.
            for (var i = 0; i < count; i++)
            {
                var weight = weights[k + i];
                var row = source.AsSpan((start + i) * stride, stride);
                for (var column = 0; column < stride; column++)
                {
                    sums[column] += row[column] * weight;
                }
            }

            var output = target.AsSpan(y * stride, stride);
            for (var column = 0; column < stride; column++)
            {
                output[column] = Clip8(sums[column]);
            }
        }

        return target;
    }

    private static (int[] Bounds, int[] Weights, int KernelSize) PrecomputeWeights(int inSize, int outSize)
    {
        const double Support = 1.0;
        var scale = (double)inSize / outSize;
        var filterScale = Math.Max(scale, 1.0);
        var support = Support * filterScale;
        var kernelSize = ((int)Math.Ceiling(support) * 2) + 1;
        var bounds = new int[outSize * 2];
        var weights = new int[outSize * kernelSize];
        var kernel = new double[kernelSize];
        for (var x = 0; x < outSize; x++)
        {
            var center = (x + 0.5) * scale;
            var inverseScale = 1.0 / filterScale;
            var min = Math.Max((int)(center - support + 0.5), 0);
            var max = Math.Min((int)(center + support + 0.5), inSize) - min;
            var total = 0.0;
            for (var i = 0; i < max; i++)
            {
                var w = Triangle((i + min - center + 0.5) * inverseScale);
                kernel[i] = w;
                total += w;
            }

            for (var i = 0; i < max; i++)
            {
                var w = total == 0 ? kernel[i] : kernel[i] / total;
                weights[(x * kernelSize) + i] = (int)(w < 0 ? -0.5 + (w * (1 << PrecisionBits)) : 0.5 + (w * (1 << PrecisionBits)));
            }

            bounds[x * 2] = min;
            bounds[(x * 2) + 1] = max;
        }

        return (bounds, weights, kernelSize);
    }

    private static double Triangle(double x)
    {
        x = Math.Abs(x);
        return x < 1.0 ? 1.0 - x : 0.0;
    }

    private static byte Clip8(int value)
    {
        if (value >= 1 << PrecisionBits << 8)
        {
            return 255;
        }

        return value <= 0 ? (byte)0 : (byte)(value >> PrecisionBits);
    }
}
