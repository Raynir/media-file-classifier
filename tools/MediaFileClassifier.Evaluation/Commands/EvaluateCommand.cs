using MediaFileClassifier.Evaluation.Data;
using MediaFileClassifier.Evaluation.Reporting;
using MediaFileClassifier.Evaluation.Systems;

namespace MediaFileClassifier.Evaluation.Commands;

/// <summary>
/// Scores a results file against a data set and writes a dated report next to the earlier ones.
/// </summary>
internal static class EvaluateCommand
{
    public static async Task<int> RunAsync(CommandOptions options, TimeProvider clock, TextWriter output, CancellationToken cancellationToken)
    {
        if (options.Results is null == (options.System is null))
        {
            throw new ArgumentException("evaluate needs either --results <file> or --system <siglip2|app-hybrid|app-vector|app-keyword> --models <folder>.");
        }

        var data = Load(options);
        var errors = DataValidator.Validate(data, checkFiles: false);
        if (errors.Count > 0)
        {
            ValidateCommand.Print(errors, output);
            return 1;
        }

        var report = options.System is null
            ? await ScoreResultsFileAsync(options.Results!, data, clock, cancellationToken)
            : await ScoreAppSystemAsync(options, data, clock, output, cancellationToken);

        var directory = options.Out ?? Path.Combine(data.Directory, "reports");
        var (path, compared) = Finish(report, data, directory, output);
        output.WriteLine($"Report: {path}");
        return options.FailBelowBaseline && compared.Comparisons.Any(c => c.Status == ReportComparer.Below) ? 3 : 0;
    }

    /// <summary>
    /// Compares a report with the previous run, the system's baseline run and the baselines, writes it, and prints
    /// each headline metric with its change from the baseline run.
    /// </summary>
    /// <param name="report">The new report.</param>
    /// <param name="data">The data set.</param>
    /// <param name="directory">The reports folder.</param>
    /// <param name="output">Where the summary goes.</param>
    /// <returns>The path of the Markdown report and the compared report.</returns>
    internal static (string Path, EvaluationReport Report) Finish(EvaluationReport report, EvaluationData data, string directory, TextWriter output)
    {
        var previous = ReportStore.FindPrevious(directory, report.System, report.CreatedAt);
        var (reference, referenceName) = FindReference(data, directory, report.System, output);
        report = ReportComparer.Compare(report, previous?.Report, previous?.Name, data.Baselines, reference, referenceName);
        var path = ReportStore.Write(directory, report);
        Print(report, output);
        return (path, report);
    }

    internal static void Print(EvaluationReport report, TextWriter output)
    {
        if (report.Reference is not null)
        {
            output.WriteLine($"  vs baseline run {report.Reference}{(report.ReferenceUsedOtherInputs == true ? " (other labels)" : string.Empty)}");
        }

        foreach (var c in report.Comparisons)
        {
            var change = c.ReferenceDelta is { } delta ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{delta,+8:+0.0000;-0.0000;0.0000}") : new string(' ', 8);
            output.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"  {c.Metric,-10} {c.Group,-9} {c.Value:0.0000} {change}  {c.Status}"));
        }
    }

    private static (EvaluationReport? Report, string? Name) FindReference(EvaluationData data, string directory, string system, TextWriter output)
    {
        if (!data.Baselines.References.TryGetValue(system, out var name))
        {
            return (null, null);
        }

        var file = name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : name + ".json";
        var report = ReportStore.Read(Path.Combine(directory, file));
        if (report is null)
        {
            output.WriteLine($"Warning: the baseline run {file} of {system} is not in {directory}.");
            return (null, null);
        }

        return (report, file);
    }

    private static async Task<EvaluationReport> ScoreResultsFileAsync(string path, EvaluationData data, TimeProvider clock, CancellationToken cancellationToken)
    {
        var results = ResultsFile.Load(path);
        return await Evaluator.EvaluateAsync(
            data,
            results.Name,
            results.HasSearch ? results : null,
            results.HasOcr ? results : null,
            clock.GetUtcNow(),
            cancellationToken);
    }

    private static async Task<EvaluationReport> ScoreAppSystemAsync(CommandOptions options, EvaluationData data, TimeProvider clock, TextWriter output, CancellationToken cancellationToken)
    {
        if (options.Models is null)
        {
            throw new ArgumentException($"--system {options.System} needs --models <folder with the SigLIP 2 files>.");
        }

        if (string.Equals(options.System, "siglip2", StringComparison.OrdinalIgnoreCase))
        {
            // The embedding regression run scores the content categories only (E09-S05).
            var content = data with { Queries = [.. data.Queries.Where(q => !QueryCategories.SearchSpecific.Contains(q.Category))] };
            output.WriteLine($"siglip2 scores the content categories only: {content.Queries.Count} of {data.Queries.Count} queries.");
            using var siglip2 = new Siglip2System(options.Models, output.WriteLine);
            return await Evaluator.EvaluateAsync(content, siglip2.Name, siglip2, null, clock.GetUtcNow(), cancellationToken);
        }

        await using var system = CreateAppSystem(options, data, ParseMode(options.System!), output);
        system.Settings = RankingArguments.Parse(options);
        var report = await Evaluator.EvaluateAsync(data, system.Name, system, null, clock.GetUtcNow(), cancellationToken);
        return report with { Parameters = Parameters(system, options) };
    }

    internal static AppSearchSystem CreateAppSystem(CommandOptions options, EvaluationData data, AppSearchMode mode, TextWriter output)
    {
        var path = AnalysisPath(options, data);
        if (!File.Exists(path))
        {
            throw new ArgumentException($"No stored analysis at {path}; run 'analyze' first or give --analysis <file>.");
        }

        return new AppSearchSystem(options.Models!, CorpusAnalysis.Load(path), mode, output.WriteLine);
    }

    internal static IReadOnlyDictionary<string, string> Parameters(AppSearchSystem system, CommandOptions options) =>
        new SortedDictionary<string, string>(system.Settings.Describe().ToDictionary(), StringComparer.Ordinal)
        {
            ["analysis"] = options.Vlm ?? AnalyzeCommand.DefaultModel,
        };

    internal static AppSearchMode ParseMode(string system) => system.ToLowerInvariant() switch
    {
        "app-hybrid" => AppSearchMode.Hybrid,
        "app-vector" => AppSearchMode.Vector,
        "app-keyword" => AppSearchMode.Keyword,
        _ => throw new ArgumentException($"Unknown system '{system}'. Use siglip2, app-hybrid, app-vector or app-keyword."),
    };

    private static string AnalysisPath(CommandOptions options, EvaluationData data) =>
        options.Analysis ?? AnalyzeCommand.DefaultPath(data.Directory, options.Vlm ?? AnalyzeCommand.DefaultModel);

    internal static EvaluationData Load(CommandOptions options)
    {
        var data = EvaluationData.Load(options.Data);
        return options.Media is null ? data : data with { Info = data.Info with { MediaRoot = options.Media } };
    }
}
