using MediaFileClassifier.Evaluation.Data;

namespace MediaFileClassifier.Evaluation.Reporting;

/// <summary>
/// Adds the change from the previous run and the baseline status to a report.
/// </summary>
public static class ReportComparer
{
    public const string Pass = "pass";
    public const string Below = "below";
    public const string NoBaseline = "no baseline";

    public static EvaluationReport Compare(EvaluationReport report, EvaluationReport? previous, string? previousName, Baselines baselines)
    {
        var comparisons = new List<MetricComparison>();
        foreach (var language in report.Search?.Languages ?? [])
        {
            var before = previous?.Search?.Languages.FirstOrDefault(l => l.Language == language.Language);
            comparisons.Add(Higher("recall@10", language.Language, language.RecallAt10, before?.RecallAt10, baselines.RecallAt10));
            comparisons.Add(Higher("mrr", language.Language, language.Mrr, before?.Mrr, baselines.Mrr));
            if (language.NoneShown is { } shown)
            {
                comparisons.Add(Lower("none-shown", language.Language, shown, before?.NoneShown, baselines.NoneShown));
            }
        }

        foreach (var script in report.Ocr?.Scripts ?? [])
        {
            var before = previous?.Ocr?.Scripts.FirstOrDefault(s => s.Script == script.Script);
            comparisons.Add(Lower("ocr-cer", script.Script, script.Cer, before?.Cer, baselines.OcrCer));
        }

        return report with
        {
            Previous = previousName,
            PreviousUsedOtherInputs = previous is null ? null : !SameInputs(report.Data.InputHashes, previous.Data.InputHashes),
            Comparisons = comparisons,
        };
    }

    private static MetricComparison Higher(string metric, string group, double value, double? previous, IReadOnlyDictionary<string, double?> thresholds)
    {
        var threshold = thresholds.GetValueOrDefault(group);
        var status = threshold is null ? NoBaseline : value >= threshold ? Pass : Below;
        return new MetricComparison(metric, group, value, previous, Delta(value, previous), threshold, status);
    }

    private static MetricComparison Lower(string metric, string group, double value, double? previous, IReadOnlyDictionary<string, double?> thresholds)
    {
        var threshold = thresholds.GetValueOrDefault(group);
        var status = threshold is null ? NoBaseline : value <= threshold ? Pass : Below;
        return new MetricComparison(metric, group, value, previous, Delta(value, previous), threshold, status);
    }

    private static double? Delta(double value, double? previous) => previous is null ? null : Evaluator.Round(value - previous.Value);

    private static bool SameInputs(IReadOnlyDictionary<string, string> current, IReadOnlyDictionary<string, string> previous)
    {
        // The baselines don't change the scores, so a new threshold doesn't make the runs incomparable.
        static IEnumerable<KeyValuePair<string, string>> Scored(IReadOnlyDictionary<string, string> hashes) =>
            hashes.Where(h => h.Key != EvaluationData.BaselinesFile).OrderBy(h => h.Key, StringComparer.Ordinal);

        return Scored(current).SequenceEqual(Scored(previous));
    }
}
