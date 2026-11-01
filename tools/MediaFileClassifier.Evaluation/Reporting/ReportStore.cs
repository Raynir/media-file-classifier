using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediaFileClassifier.Evaluation.Data;

namespace MediaFileClassifier.Evaluation.Reporting;

/// <summary>
/// The folder of dated reports: <c>yyyy-MM-dd_HHmmss_system.json</c> plus the same name in <c>.md</c>.
/// </summary>
public static partial class ReportStore
{
    public static string FileStem(EvaluationReport report) =>
        string.Create(CultureInfo.InvariantCulture, $"{report.CreatedAt.UtcDateTime:yyyy-MM-dd_HHmmss}_{Slug(report.System)}");

    /// <summary>
    /// The latest earlier report of the same system, or null.
    /// </summary>
    public static (EvaluationReport Report, string Name)? FindPrevious(string directory, string system, DateTimeOffset before)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        return Directory.EnumerateFiles(directory, "*.json")
            .Select(path => (Report: Read(path), Name: Path.GetFileName(path)))
            .Where(r => r.Report is not null && r.Report.System == system && r.Report.CreatedAt < before)
            .OrderByDescending(r => r.Report!.CreatedAt)
            .Select(r => ((EvaluationReport, string)?)(r.Report!, r.Name))
            .FirstOrDefault();
    }

    public static EvaluationReport? Read(string path)
    {
        try
        {
            return EvaluationJson.Read<EvaluationReport>(path);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            // Not a report, e.g. notes kept next to the reports.
            return null;
        }
    }

    /// <summary>
    /// Writes the JSON and Markdown files and returns the path of the Markdown one. A second report
    /// of the same system in the same second gets <c>-2</c>, <c>-3</c>… instead of replacing the first.
    /// </summary>
    public static string Write(string directory, EvaluationReport report)
    {
        Directory.CreateDirectory(directory);
        var baseStem = Path.Combine(directory, FileStem(report));
        var stem = baseStem;
        for (var n = 2; File.Exists(stem + ".json") || File.Exists(stem + ".md"); n++)
        {
            stem = string.Create(CultureInfo.InvariantCulture, $"{baseStem}-{n}");
        }

        File.WriteAllText(stem + ".json", JsonSerializer.Serialize(report, EvaluationJson.Options) + "\n");
        File.WriteAllText(stem + ".md", ReportWriter.Write(report));
        return stem + ".md";
    }

    private static string Slug(string value) => NotSlug().Replace(value.ToLowerInvariant(), "-").Trim('-');

    [GeneratedRegex("[^a-z0-9.]+")]
    private static partial Regex NotSlug();
}
