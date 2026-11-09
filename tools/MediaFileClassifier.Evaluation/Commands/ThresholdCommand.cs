using System.Globalization;
using MediaFileClassifier.Evaluation.Data;
using MediaFileClassifier.Evaluation.Systems;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.UseCases.Search.SearchMedia;

namespace MediaFileClassifier.Evaluation.Commands;

/// <summary>
/// Measures how the semantic relevance threshold (E09-S01-T03) would treat the corpus: for each candidate threshold,
/// how many relevant items of the real queries stay visible and how many items nonsense queries still show.
/// </summary>
internal static class ThresholdCommand
{
    /// <summary>
    /// Queries that mean nothing, in Latin and Cyrillic letters. None of them should find a strong match.
    /// </summary>
    internal static readonly IReadOnlyList<string> NonsenseQueries =
    [
        "asdfghjkl",
        "qwertyuiop",
        "xqzvbn plorft",
        "blorptastic grimble",
        "zzzzzz",
        "kjh8 9sdf",
        "xyzzy frobnicate",
        "lorem ipsum dolor",
        "1234567890",
        "glorp snarf wib",
        "фывапролдж",
        "йцукенгшщз",
        "ыдвлао ыдлв",
        "кракозябра бумс",
        "шмырк трямп",
        "!!! ???",
        "aaaa bbbb cccc",
        "qqq",
        "mxyzptlk",
        "вжух пщщ",
    ];

    public static async Task<int> RunAsync(CommandOptions options, TextWriter output, CancellationToken cancellationToken)
    {
        if (options.Models is null)
        {
            throw new ArgumentException("threshold needs --models <folder with the SigLIP 2 files>.");
        }

        var data = EvaluateCommand.Load(options);
        using var system = new Siglip2System(options.Models, output.WriteLine);
        await system.IndexAsync(data.Items, data.MediaRoot, cancellationToken);

        var scored = new List<(EvalQuery Query, IReadOnlyDictionary<string, float> Scores)>();
        foreach (var query in data.Queries)
        {
            scored.Add((query, await system.ScoreAsync(query.Text, cancellationToken)));
        }

        var nonsense = new List<(string Text, IReadOnlyDictionary<string, float> Scores)>();
        foreach (var text in NonsenseQueries)
        {
            nonsense.Add((text, await system.ScoreAsync(text, cancellationToken)));
        }

        Report(output, "cosine similarity", scored, nonsense, [.. Enumerable.Range(0, 26).Select(i => i / 100.0)], (value, _) => value);
        Report(output, "cosine minus the query's mean over the library", scored, nonsense, [.. Enumerable.Range(0, 21).Select(i => i / 200.0)], (value, stats) => value - stats.Mean);
        Report(output, "z-score over the library", scored, nonsense, [.. Enumerable.Range(0, 31).Select(i => i / 5.0)], (value, stats) => (value - stats.Mean) / stats.Deviation);
        ReportCut(output, SemanticRelevanceThresholds.Siglip2Base, scored, nonsense);
        return 0;
    }

    // The app's rule (SemanticRelevanceThresholds): what the search would hide on this corpus.
    private static void ReportCut(
        TextWriter output,
        SemanticRelevanceCut cut,
        IReadOnlyList<(EvalQuery Query, IReadOnlyDictionary<string, float> Scores)> real,
        IReadOnlyList<(string Text, IReadOnlyDictionary<string, float> Scores)> nonsense)
    {
        static float Mean(IReadOnlyDictionary<string, float> scores) => scores.Values.Average();

        var inv = CultureInfo.InvariantCulture;
        output.WriteLine();
        output.WriteLine(string.Format(inv, "== app rule: centred >= {0:0.000} or raw >= {1:0.000} ==", cut.MinCentredSimilarity, cut.MinSimilarity));
        foreach (var group in real.GroupBy(r => r.Query.Language))
        {
            var top10Kept = 0;
            var top10All = 0;
            var withResult = 0;
            foreach (var (query, scores) in group)
            {
                var mean = Mean(scores);
                var top10 = scores.OrderByDescending(pair => pair.Value).Take(10).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
                var relevantInTop10 = query.Relevant.Where(top10.Contains).ToList();
                top10All += relevantInTop10.Count;
                top10Kept += relevantInTop10.Count(id => cut.IsRelevant(scores[id], mean));
                withResult += scores.Values.Any(value => cut.IsRelevant(value, mean)) ? 1 : 0;
            }

            output.WriteLine(string.Format(inv, "  {0}: relevant-in-top-10 kept {1:P1}, queries with a result {2:P1}", group.Key, (double)top10Kept / top10All, (double)withResult / group.Count()));
        }

        var shown = nonsense.Select(n => n.Scores.Values.Count(value => cut.IsRelevant(value, Mean(n.Scores)))).ToList();
        output.WriteLine(string.Format(inv, "  nonsense: {0} of {1} queries show a result, {2:0.00} results on average", shown.Count(count => count > 0), shown.Count, shown.Average()));
    }

    private static void Report(
        TextWriter output,
        string title,
        IReadOnlyList<(EvalQuery Query, IReadOnlyDictionary<string, float> Scores)> real,
        IReadOnlyList<(string Text, IReadOnlyDictionary<string, float> Scores)> nonsense,
        IReadOnlyList<double> thresholds,
        Func<double, (double Mean, double Deviation), double> transform)
    {
        static (double Mean, double Deviation) Stats(IReadOnlyDictionary<string, float> scores)
        {
            var mean = scores.Values.Average(v => (double)v);
            return (mean, Math.Sqrt(scores.Values.Average(v => (v - mean) * (v - mean))));
        }

        var realScored = real.Select(r =>
        {
            var stats = Stats(r.Scores);
            var all = r.Scores.ToDictionary(pair => pair.Key, pair => transform(pair.Value, stats), StringComparer.Ordinal);
            var top10 = all.OrderByDescending(pair => pair.Value).Take(10).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
            return (r.Query.Language, Top: all.Values.Max(), Relevant: r.Query.Relevant.Where(all.ContainsKey).Select(id => all[id]).ToList(), Top10: r.Query.Relevant.Where(top10.Contains).Select(id => all[id]).ToList());
        }).ToList();
        var nonsenseScored = nonsense.Select(n =>
        {
            var stats = Stats(n.Scores);
            return (n.Text, Values: n.Scores.Values.Select(v => transform(v, stats)).OrderDescending().ToList());
        }).ToList();

        var inv = CultureInfo.InvariantCulture;
        output.WriteLine();
        output.WriteLine($"== {title} ==");
        foreach (var group in realScored.GroupBy(r => r.Language))
        {
            var all = group.SelectMany(r => r.Relevant).Order().ToArray();
            var tops = group.Select(r => r.Top).Order().ToArray();
            output.WriteLine(string.Format(inv, "  {0} relevant: min {1:0.000} p5 {2:0.000} p10 {3:0.000} median {4:0.000}; top-1 min {5:0.000} median {6:0.000}", group.Key, all[0], Percentile(all, 0.05), Percentile(all, 0.10), Percentile(all, 0.5), tops[0], Percentile(tops, 0.5)));
        }

        var nonsenseTops = nonsenseScored.Select(n => n.Values[0]).Order().ToArray();
        output.WriteLine(string.Format(inv, "  nonsense top-1: min {0:0.000} median {1:0.000} max {2:0.000}", nonsenseTops[0], Percentile(nonsenseTops, 0.5), nonsenseTops[^1]));
        output.WriteLine("  threshold | relevant kept en / ru | relevant-in-top-10 kept en / ru | real queries with a result en / ru | nonsense with a result | nonsense mean results");
        foreach (var t in thresholds)
        {
            double Kept(IEnumerable<double> values)
            {
                var list = values.ToList();
                return list.Count == 0 ? 1 : (double)list.Count(v => v >= t) / list.Count;
            }

            var en = realScored.Where(r => r.Language == "en").ToList();
            var ru = realScored.Where(r => r.Language == "ru").ToList();
            output.WriteLine(string.Format(
                inv,
                "  {0,9:0.000} | {1,8:P1} / {2,6:P1} | {3,14:P1} / {4,6:P1} | {5,14:P1} / {6,6:P1} | {7,22:P0} | {8,6:0.0}",
                t,
                Kept(en.SelectMany(r => r.Relevant)),
                Kept(ru.SelectMany(r => r.Relevant)),
                Kept(en.SelectMany(r => r.Top10)),
                Kept(ru.SelectMany(r => r.Top10)),
                (double)en.Count(r => r.Top >= t) / en.Count,
                (double)ru.Count(r => r.Top >= t) / ru.Count,
                (double)nonsenseScored.Count(n => n.Values[0] >= t) / nonsenseScored.Count,
                nonsenseScored.Average(n => n.Values.Count(s => s >= t))));
        }
    }

    private static double Percentile(double[] sorted, double share) => sorted[(int)Math.Clamp(Math.Round(share * (sorted.Length - 1)), 0, sorted.Length - 1)];
}
