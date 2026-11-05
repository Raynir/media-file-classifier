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
            throw new ArgumentException("evaluate needs either --results <file> or --system siglip2 --models <folder>.");
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
        var previous = ReportStore.FindPrevious(directory, report.System, report.CreatedAt);
        report = ReportComparer.Compare(report, previous?.Report, previous?.Name, data.Baselines);
        var path = ReportStore.Write(directory, report);

        output.WriteLine($"Report: {path}");
        foreach (var c in report.Comparisons)
        {
            output.WriteLine($"  {c.Metric,-10} {c.Group,-9} {c.Value:0.0000}  {c.Status}");
        }

        return options.FailBelowBaseline && report.Comparisons.Any(c => c.Status == ReportComparer.Below) ? 3 : 0;
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
        if (!string.Equals(options.System, "siglip2", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unknown system '{options.System}'. The only one is 'siglip2'.");
        }

        if (options.Models is null)
        {
            throw new ArgumentException("--system siglip2 needs --models <folder with the SigLIP 2 files>.");
        }

        using var system = new Siglip2System(options.Models, output.WriteLine);
        return await Evaluator.EvaluateAsync(data, system.Name, system, null, clock.GetUtcNow(), cancellationToken);
    }

    internal static EvaluationData Load(CommandOptions options)
    {
        var data = EvaluationData.Load(options.Data);
        return options.Media is null ? data : data with { Info = data.Info with { MediaRoot = options.Media } };
    }
}
