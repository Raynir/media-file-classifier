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
        if (options.Results is null)
        {
            throw new ArgumentException("evaluate needs --results <file>.");
        }

        var data = Load(options);
        var errors = DataValidator.Validate(data, checkFiles: false);
        if (errors.Count > 0)
        {
            ValidateCommand.Print(errors, output);
            return 1;
        }

        var results = ResultsFile.Load(options.Results);
        var report = await Evaluator.EvaluateAsync(
            data,
            results.Name,
            results.HasSearch ? results : null,
            results.HasOcr ? results : null,
            clock.GetUtcNow(),
            cancellationToken);

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

    internal static EvaluationData Load(CommandOptions options)
    {
        var data = EvaluationData.Load(options.Data);
        return options.Media is null ? data : data with { Info = data.Info with { MediaRoot = options.Media } };
    }
}
