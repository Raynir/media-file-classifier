using MediaFileClassifier.Evaluation.Data;
using MediaFileClassifier.Evaluation.Metrics;
using MediaFileClassifier.Evaluation.Systems;

namespace MediaFileClassifier.Evaluation.Reporting;

/// <summary>
/// Runs the systems under evaluation over a data set and scores them.
/// </summary>
public static class Evaluator
{
    public const int RecallCutoff = 10;

    /// <summary>
    /// How many results the runner asks for per query. MRR is computed over this list.
    /// </summary>
    public const int ResultLimit = 100;

    public static async Task<EvaluationReport> EvaluateAsync(
        EvaluationData data,
        string systemName,
        IRetrievalSystem? search,
        IOcrEngine? ocr,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var known = data.Items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        return new EvaluationReport
        {
            System = systemName,
            CreatedAt = createdAt,
            Data = new DataSummary(
                data.Info.Name,
                data.Items.Count,
                data.Queries.GroupBy(q => q.Language).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()),
                data.OcrTruth.Count,
                data.InputHashes),
            Search = search is null ? null : await ScoreSearchAsync(data, search, known, cancellationToken),
            Ocr = ocr is null ? null : await ScoreOcrAsync(data, ocr, cancellationToken),
        };
    }

    private static async Task<SearchSection> ScoreSearchAsync(
        EvaluationData data, IRetrievalSystem system, HashSet<string> known, CancellationToken cancellationToken)
    {
        await system.IndexAsync(data.Items, data.MediaRoot, cancellationToken);
        // Averages use the unrounded values; only what is stored is rounded.
        var scores = new List<(QueryScore Score, double Recall, double Rank)>();
        foreach (var query in data.Queries.OrderBy(q => q.Id, StringComparer.Ordinal))
        {
            var ranked = await system.SearchAsync(query, ResultLimit, cancellationToken);
            var relevant = query.Relevant.ToHashSet(StringComparer.Ordinal);

            // A query that should return nothing has no relevant items, so recall and MRR don't apply to it.
            var none = IsNone(query.Category);
            var recall = none ? 0 : RetrievalMetrics.RecallAt(RecallCutoff, ranked, relevant);
            var rank = none ? 0 : RetrievalMetrics.ReciprocalRank(ranked, relevant);
            var score = new QueryScore(
                query.Id,
                query.Language,
                query.Category,
                relevant.Count,
                ranked.Count,
                RetrievalMetrics.FirstRelevantRank(ranked, relevant),
                Round(recall),
                Round(rank),
                [.. ranked.Where(id => !known.Contains(id)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
            scores.Add((score, recall, rank));
        }

        var languages = scores
            .GroupBy(s => s.Score.Language)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var ranked = g.Where(s => !IsNone(s.Score.Category)).ToList();
                var none = g.Where(s => IsNone(s.Score.Category)).ToList();
                return new LanguageScore(
                    g.Key,
                    ranked.Count,
                    ranked.Count == 0 ? 0 : Mean(ranked, s => s.Recall),
                    ranked.Count == 0 ? 0 : Mean(ranked, s => s.Rank),
                    none.Count,
                    Shown(none));
            })
            .ToList();
        var categories = scores
            .GroupBy(s => (s.Score.Language, s.Score.Category))
            .OrderBy(g => g.Key.Category, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Language, StringComparer.Ordinal)
            .Select(g => new CategoryScore(
                g.Key.Language,
                g.Key.Category,
                g.Count(),
                Mean(g, s => s.Recall),
                Mean(g, s => s.Rank),
                IsNone(g.Key.Category) ? Shown([.. g]) : null))
            .ToList();
        return new SearchSection(ResultLimit, languages, categories, [.. scores.Select(s => s.Score)]);
    }

    private static async Task<OcrSection> ScoreOcrAsync(EvaluationData data, IOcrEngine engine, CancellationToken cancellationToken)
    {
        var items = data.Items.ToDictionary(i => i.Id, StringComparer.Ordinal);
        var images = new List<ImageScore>();
        foreach (var truth in data.OcrTruth.OrderBy(t => t.Id, StringComparer.Ordinal))
        {
            var item = items[truth.Id];
            var path = Path.Combine(data.MediaRoot, item.File);
            var text = await engine.RecognizeAsync(item, path, cancellationToken);
            var (edits, length) = CharacterErrorRate.Measure(truth.Text, text);
            images.Add(new ImageScore(truth.Id, truth.ScriptGroup, truth.Origin, length, edits, Round(Ratio(edits, length)), text is null));
        }

        var scripts = images
            .GroupBy(i => i.Script)
            .Select(g => Script(g.Key, [.. g]))
            .Append(Script("all", images))
            .OrderBy(s => s.Script == "all" ? 1 : 0)
            .ThenBy(s => s.Script, StringComparer.Ordinal)
            .ToList();
        return new OcrSection(scripts, images);
    }

    private static ScriptScore Script(string name, IReadOnlyList<ImageScore> images)
    {
        var edits = images.Sum(i => i.Edits);
        var length = images.Sum(i => i.ReferenceCharacters);
        return new ScriptScore(name, images.Count, length, edits, Round(Ratio(edits, length)));
    }

    private static double Ratio(int edits, int length) => length == 0 ? (edits == 0 ? 0 : 1) : (double)edits / length;

    private static bool IsNone(string category) => category == QueryCategories.None;

    private static double? Shown(IReadOnlyList<(QueryScore Score, double Recall, double Rank)> queries) =>
        queries.Count == 0 ? null : Round((double)queries.Count(q => q.Score.Returned > 0) / queries.Count);

    private static double Mean<T>(IEnumerable<T> values, Func<T, double> selector) => Round(values.Average(selector));

    internal static double Round(double value) => Math.Round(value, 4, MidpointRounding.ToEven);
}
