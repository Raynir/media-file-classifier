using MediaFileClassifier.Benchmarks.Measurement;

namespace MediaFileClassifier.Benchmarks.Production;

/// <summary>
/// Simulates typing the benchmark queries with a given typing pause (MFC-990), from measured search and encoder times.
/// The gaps between keystrokes are drawn from a log-normal distribution. A search starts once a gap is longer than the
/// pause, and after the last keystroke. As in the app, typing doesn't cancel a running search; the next search that
/// starts does, so a search counts as cancelled when the next one of the same query starts before it is done.
/// </summary>
internal static class TypingSimulation
{
    /// <summary>
    /// Typed queries simulated per query text.
    /// </summary>
    public const int Trials = 200;

    /// <summary>
    /// Spread (sigma) of the log-normal keystroke gaps.
    /// </summary>
    public const double GapSigma = 0.5;

    /// <summary>
    /// Simulates every query text.
    /// </summary>
    /// <param name="queries">For each query text, the measured times (encoder plus search, ms) of each prefix, by
    /// prefix length (index 0 is one letter).</param>
    /// <param name="pauseMs">The typing pause.</param>
    /// <param name="medianGapMs">The median gap between keystrokes.</param>
    /// <param name="seed">Random seed, the same for every pause so they type the same gaps.</param>
    /// <returns>The result.</returns>
    public static Result Run(IReadOnlyList<IReadOnlyList<IReadOnlyList<double>>> queries, double pauseMs, double medianGapMs, int seed)
    {
        var random = new Random(seed);
        var mu = Math.Log(medianGapMs);
        var started = 0;
        var cancelled = 0;
        var typed = 0;
        var resultTimes = new List<double>();
        foreach (var prefixes in queries)
        {
            for (var trial = 0; trial < Trials; trial++)
            {
                typed++;
                var time = 0.0;
                var previousEnd = double.NaN;
                for (var length = 1; length <= prefixes.Count; length++)
                {
                    var last = length == prefixes.Count;
                    var gap = last ? double.PositiveInfinity : Math.Exp(mu + (GapSigma * NextGaussian(random)));
                    var times = prefixes[length - 1];
                    var duration = times[random.Next(times.Count)];
                    if (gap > pauseMs)
                    {
                        var start = time + pauseMs;
                        started++;
                        if (previousEnd > start)
                        {
                            cancelled++;
                        }

                        previousEnd = start + duration;
                        if (last)
                        {
                            resultTimes.Add(pauseMs + duration);
                        }
                    }

                    time += last ? 0 : gap;
                }
            }
        }

        return new Result(started / (double)typed, cancelled / (double)typed, LatencySummary.From(resultTimes));
    }

    private static double NextGaussian(Random random) =>
        Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());

    /// <summary>
    /// The simulated numbers.
    /// </summary>
    /// <param name="SearchesPerQuery">Searches started per typed query.</param>
    /// <param name="CancelledPerQuery">Searches cancelled per typed query.</param>
    /// <param name="LastKeystrokeToResults">From the last keystroke to the final results: the pause, the encoder and
    /// the search.</param>
    public sealed record Result(double SearchesPerQuery, double CancelledPerQuery, LatencySummary LastKeystrokeToResults);
}
