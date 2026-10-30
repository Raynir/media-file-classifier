namespace MediaFileClassifier.Benchmarks.Measurement;

/// <summary>
/// Query latency percentiles, in milliseconds.
/// </summary>
internal sealed record LatencySummary(int Count, double Mean, double P50, double P95, double P99, double Max)
{
    public static LatencySummary From(IReadOnlyList<double> milliseconds)
    {
        if (milliseconds.Count == 0)
        {
            return new LatencySummary(0, 0, 0, 0, 0, 0);
        }

        var sorted = milliseconds.Order().ToArray();
        return new LatencySummary(
            sorted.Length,
            sorted.Average(),
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95),
            Percentile(sorted, 0.99),
            sorted[^1]);
    }

    // Nearest-rank percentile.
    private static double Percentile(double[] sorted, double percentile)
    {
        var rank = (int)Math.Ceiling(percentile * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }
}
