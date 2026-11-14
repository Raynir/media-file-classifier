using System.Globalization;
using System.Text;
using MediaFileClassifier.Evaluation.Data;
using MediaFileClassifier.Evaluation.Reporting;
using MediaFileClassifier.Evaluation.Systems;
using MediaFileClassifier.Infrastructure.Abstractions.Search;

namespace MediaFileClassifier.Evaluation.Commands;

/// <summary>
/// The tuning pass (E09-S05-T03): indexes the corpus once in the app's search, then scores a fixed grid of RRF k, list
/// weights, BM25 field weights and relevance cuts, in hybrid mode, and each cut in vector-only mode for comparison. The
/// grid is bounded on purpose; it prints a ranked table and writes it next to the reports.
/// </summary>
internal static class TuneCommand
{
    private static readonly double[] RrfKs = [10, 30, 60];

    private static readonly double[] KeywordWeights = [0.5, 1, 1.5, 2];

    private static readonly (string Name, string Weights)[] FieldWeightSets =
    [
        ("untuned", "title=4,description=1,tags=3,ocr=1,transcript=1,other=1,filename=2"),
        ("flat", "title=1,description=1,tags=1,ocr=1,transcript=1,other=1,filename=1"),
        ("text-up", "title=4,description=2,tags=3,ocr=2,transcript=1,other=1,filename=2"),
        ("name-tags-up", "title=4,description=1,tags=5,ocr=1,transcript=1,other=1,filename=5"),
    ];

    private static readonly string[] Thresholds = ["0.07,0.13", "0.05,0.12", "0.09,0.15"];

    public static async Task<int> RunAsync(CommandOptions options, TimeProvider clock, TextWriter output, CancellationToken cancellationToken)
    {
        if (options.Models is null)
        {
            throw new ArgumentException("tune needs --models <folder with the SigLIP 2 files>.");
        }

        var data = EvaluateCommand.Load(options);
        var errors = DataValidator.Validate(data, checkFiles: false);
        if (errors.Count > 0)
        {
            ValidateCommand.Print(errors, output);
            return 1;
        }

        await using var system = EvaluateCommand.CreateAppSystem(options, data, AppSearchMode.Hybrid, output);
        var rows = new List<Row>();
        var started = clock.GetUtcNow();
        foreach (var threshold in Thresholds)
        {
            var cut = RankingArguments.ParseThreshold(threshold);
            system.Mode = AppSearchMode.Vector;
            system.Settings = new AppSearchSettings(SearchRankingOptions.Default, cut);
            rows.Add(await ScoreAsync(system, data, "vector", "-", "-", "-", threshold, started, cancellationToken).ConfigureAwait(false));
        }

        system.Mode = AppSearchMode.Hybrid;
        foreach (var k in RrfKs)
        {
            foreach (var weight in KeywordWeights)
            {
                foreach (var (name, weights) in FieldWeightSets)
                {
                    foreach (var threshold in Thresholds)
                    {
                        system.Settings = new AppSearchSettings(
                            SearchRankingOptions.Default with
                            {
                                RrfK = k,
                                KeywordWeight = weight,
                                FieldWeights = RankingArguments.ParseFieldWeights(weights, KeywordFieldWeights.Default),
                            },
                            RankingArguments.ParseThreshold(threshold));
                        rows.Add(await ScoreAsync(system, data, "hybrid", Text(k), Text(weight), name, threshold, started, cancellationToken).ConfigureAwait(false));
                    }
                }
            }

            output.WriteLine($"  scored rrf-k {Text(k)}");
        }

        var table = Write(rows, data);
        var directory = options.Out ?? Path.Combine(data.Directory, "reports");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"{started.UtcDateTime:yyyy-MM-dd_HHmmss}_tune-app-hybrid.md"));
        await File.WriteAllTextAsync(path, table, cancellationToken).ConfigureAwait(false);
        output.WriteLine(table);
        output.WriteLine($"Table: {path}");
        return 0;
    }

    private static async Task<Row> ScoreAsync(
        AppSearchSystem system,
        EvaluationData data,
        string mode,
        string k,
        string weight,
        string fields,
        string threshold,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var report = await Evaluator.EvaluateAsync(data, system.Name, system, null, at, cancellationToken).ConfigureAwait(false);
        var languages = report.Search!.Languages.ToDictionary(l => l.Language, StringComparer.Ordinal);
        return new Row(mode, k, weight, fields, threshold, languages["en"], languages["ru"]);
    }

    private static string Write(List<Row> rows, EvaluationData data)
    {
        var text = new StringBuilder();
        text.Append("# Tuning pass: app-hybrid (E09-S05-T03)\n\n");
        text.Append(string.Create(CultureInfo.InvariantCulture, $"Corpus {data.Items.Count} items, queries {data.Queries.Count(q => q.Language == "en")} en / {data.Queries.Count(q => q.Language == "ru")} ru. "));
        text.Append("Ranked by mean recall@10 over both languages, then mean MRR, then mean none-shown (lower first). ");
        text.Append("Field weight sets: ");
        text.Append(string.Join("; ", FieldWeightSets.Select(f => $"{f.Name} = {f.Weights}")));
        text.Append(string.Create(
            CultureInfo.InvariantCulture,
            $". Vector weight {SearchRankingOptions.Default.VectorWeight}, {SearchRankingOptions.Default.CandidateLimit} candidates per list in every row.\n\n"));
        text.Append("| Mode | RRF k | Keyword weight | Field weights | Threshold | Recall@10 en | Recall@10 ru | MRR en | MRR ru | None-shown en | None-shown ru |\n");
        text.Append("|---|--:|--:|---|---|--:|--:|--:|--:|--:|--:|\n");
        foreach (var row in rows
            .OrderByDescending(r => r.En.RecallAt10 + r.Ru.RecallAt10)
            .ThenByDescending(r => r.En.Mrr + r.Ru.Mrr)
            .ThenBy(r => (r.En.NoneShown ?? 0) + (r.Ru.NoneShown ?? 0))
            .ThenBy(r => r.Mode, StringComparer.Ordinal))
        {
            text.Append(string.Create(
                CultureInfo.InvariantCulture,
                $"| {row.Mode} | {row.K} | {row.Weight} | {row.Fields} | {row.Threshold} | {row.En.RecallAt10:0.0000} | {row.Ru.RecallAt10:0.0000} | {row.En.Mrr:0.0000} | {row.Ru.Mrr:0.0000} | {row.En.NoneShown:0.0000} | {row.Ru.NoneShown:0.0000} |\n"));
        }

        return text.ToString();
    }

    private static string Text(double value) => value.ToString(CultureInfo.InvariantCulture);

    private sealed record Row(string Mode, string K, string Weight, string Fields, string Threshold, LanguageScore En, LanguageScore Ru);
}
