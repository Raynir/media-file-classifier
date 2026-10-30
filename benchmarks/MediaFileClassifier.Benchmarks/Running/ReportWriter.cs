using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using MediaFileClassifier.Benchmarks.Data;
using MediaFileClassifier.Benchmarks.Engines;

namespace MediaFileClassifier.Benchmarks.Running;

/// <summary>
/// Formats the results as Markdown, ready to paste into docs/spikes.
/// </summary>
internal static class ReportWriter
{
    private const double Megabyte = 1024 * 1024;

    public static string Write(BenchmarkOptions options, IReadOnlyList<LibraryReport> libraries)
    {
        var text = new StringBuilder();
        var sqlite = libraries.Select(l => l.Info.GetValueOrDefault("sqlite_version")).FirstOrDefault() ?? "?";
        var vec = libraries.Select(l => l.Info.GetValueOrDefault("sqlite_vec_version")).FirstOrDefault() ?? "?";
        Line(text, $"# Vector search benchmark");
        text.AppendLine();
        Line(text, $"- Machine: {Environment.MachineName}, {RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}, {Environment.ProcessorCount} logical CPUs");
        Line(text, $"- Runtime: .NET {Environment.Version}, SQLite {sqlite}, sqlite-vec {vec}");
        Line(text, $"- Queries: {options.Queries} per filter (time budget {options.Budget.TotalSeconds:F0} s), top {options.K} items, {options.Dimensions} dimensions");
        Line(text, $"- Latency is in ms. Memory is the growth over the process baseline, in MB. Recall is against {EngineCatalog.Reference}.");
        text.AppendLine();

        foreach (var library in libraries)
        {
            var spec = library.Spec;
            Line(text, $"## {spec.Items:N0} items x {spec.VectorsPerItem} vectors = {spec.Vectors:N0} vectors");
            text.AppendLine();
            Line(text, $"Database {library.DatabaseBytes / Megabyte:N0} MB (text {Long(library.Info, "text_bytes") / Megabyte:N0} MB). Generated in {Long(library.Info, "base_insert_ms") / 1000.0:N1} s; vec0 table built in {Long(library.Info, "vec0_build_ms") / 1000.0:N1} s.");
            text.AppendLine();
            text.AppendLine("| Engine | Load | First query | No filter p50 / p95 | Broad p50 / p95 | Narrow p50 / p95 | Managed | Private | SQLite heap | Recall |");
            text.AppendLine("|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|");
            var reference = library.Results.FirstOrDefault(r => r.Engine == EngineCatalog.Reference);
            foreach (var result in library.Results)
            {
                var managed = (result.AfterLoad.ManagedBytes - result.Baseline.ManagedBytes) / Megabyte;
                var privateBytes = (result.AfterQueries.PrivateBytes - result.Baseline.PrivateBytes) / Megabyte;
                var sqliteHeap = result.AfterQueries.SqliteBytes / Megabyte;
                Line(
                    text,
                    $"| {result.Engine} | {result.LoadMs:N0} | {result.FirstQueryMs:N1} | {Latency(result, FilterScenario.None)} | {Latency(result, FilterScenario.Broad)} | {Latency(result, FilterScenario.Narrow)} | {managed:N0} | {privateBytes:N0} | {sqliteHeap:N1} | {Recall(reference, result)} |");
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    private static string Latency(WorkerResult result, FilterScenario filter)
    {
        var scenario = result.Scenarios.FirstOrDefault(s => s.Filter == filter);
        return scenario is null ? "-" : string.Create(CultureInfo.InvariantCulture, $"{scenario.Latency.P50:F2} / {scenario.Latency.P95:F2}");
    }

    // The share of the reference's top-k items that the engine also returned, over all queries
    // and filters both ran.
    private static string Recall(WorkerResult? reference, WorkerResult result)
    {
        if (reference is null)
        {
            return "-";
        }

        long expected = 0;
        long found = 0;
        foreach (var scenario in result.Scenarios)
        {
            var expectedScenario = reference.Scenarios.FirstOrDefault(s => s.Filter == scenario.Filter);
            if (expectedScenario is null)
            {
                continue;
            }

            var queries = Math.Min(scenario.Results.Count, expectedScenario.Results.Count);
            for (var q = 0; q < queries; q++)
            {
                var truth = expectedScenario.Results[q];
                expected += truth.Length;
                found += truth.Intersect(scenario.Results[q]).Count();
            }
        }

        return expected == 0 ? "-" : string.Create(CultureInfo.InvariantCulture, $"{(double)found / expected:F4}");
    }

    private static long Long(IReadOnlyDictionary<string, string> info, string key) =>
        info.TryGetValue(key, out var value) ? long.Parse(value, CultureInfo.InvariantCulture) : 0;

    private static void Line(StringBuilder text, FormattableString line) =>
        text.AppendLine(line.ToString(CultureInfo.InvariantCulture));
}
