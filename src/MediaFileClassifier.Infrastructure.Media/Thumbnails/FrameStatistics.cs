using SkiaSharp;

namespace MediaFileClassifier.Infrastructure.Media.Thumbnails;

/// <summary>
/// Brightness and variation of a frame, for skipping blank frames (E03-S04-T02, T03). Measured on a 32 × 32 grid of
/// samples, with transparent pixels counted as black.
/// </summary>
/// <param name="MeanLuma">Mean luma, 0–255.</param>
/// <param name="Deviation">The largest standard deviation of the red, green and blue channels, 0–255.</param>
internal readonly record struct FrameStatistics(double MeanLuma, double Deviation)
{
    /// <summary>
    /// Below this deviation a frame is one flat colour, such as an empty first frame or a title card.
    /// </summary>
    public const double UniformDeviation = 3;

    /// <summary>
    /// Below this mean luma a video frame is nearly black, such as a fade-in.
    /// </summary>
    public const double DarkLuma = 16;

    private const int Grid = 32;

    /// <summary>
    /// Gets a value indicating whether the frame is one flat colour.
    /// </summary>
    public bool IsUniform => Deviation < UniformDeviation;

    /// <summary>
    /// Gets a value indicating whether a video frame is blank: nearly black or one flat colour.
    /// </summary>
    public bool IsBlankVideoFrame => IsUniform || MeanLuma < DarkLuma;

    /// <summary>
    /// Measures a frame.
    /// </summary>
    /// <param name="bitmap">The frame.</param>
    /// <returns>The statistics.</returns>
    public static FrameStatistics Measure(SKBitmap bitmap)
    {
        Span<double> sum = stackalloc double[3];
        Span<double> sumOfSquares = stackalloc double[3];
        double luma = 0;
        var count = 0;
        for (var row = 0; row < Grid; row++)
        {
            var y = (int)((row + 0.5) * bitmap.Height / Grid);
            for (var column = 0; column < Grid; column++)
            {
                var color = bitmap.GetPixel((int)((column + 0.5) * bitmap.Width / Grid), y);
                var alpha = color.Alpha / 255.0;
                var red = color.Red * alpha;
                var green = color.Green * alpha;
                var blue = color.Blue * alpha;
                Accumulate(sum, sumOfSquares, 0, red);
                Accumulate(sum, sumOfSquares, 1, green);
                Accumulate(sum, sumOfSquares, 2, blue);
                luma += (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
                count++;
            }
        }

        double deviation = 0;
        for (var channel = 0; channel < 3; channel++)
        {
            var mean = sum[channel] / count;
            deviation = Math.Max(deviation, Math.Sqrt(Math.Max(0, (sumOfSquares[channel] / count) - (mean * mean))));
        }

        return new(luma / count, deviation);
    }

    private static void Accumulate(Span<double> sum, Span<double> sumOfSquares, int channel, double value)
    {
        sum[channel] += value;
        sumOfSquares[channel] += value * value;
    }
}
