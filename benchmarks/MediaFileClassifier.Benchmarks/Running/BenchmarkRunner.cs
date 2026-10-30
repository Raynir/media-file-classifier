using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using MediaFileClassifier.Benchmarks.Data;

namespace MediaFileClassifier.Benchmarks.Running;

/// <summary>
/// Runs every engine on every library, each in a worker process of its own, and writes the report.
/// </summary>
internal static class BenchmarkRunner
{
    public static int Run(BenchmarkOptions options)
    {
        Directory.CreateDirectory(options.OutputDirectory);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var workDirectory = Path.Combine(options.OutputDirectory, stamp);
        Directory.CreateDirectory(workDirectory);

        var libraries = new List<LibraryReport>();
        foreach (var spec in options.Libraries)
        {
            var databasePath = LibraryGenerator.Ensure(spec, options.DataDirectory, options.Rebuild, Console.Out);
            Dictionary<string, string> info;
            using (var connection = SqliteDatabase.Open(databasePath, loadVec: false, readOnly: true))
            {
                info = SqliteDatabase.ReadInfo(connection);
            }

            var results = new List<WorkerResult>();
            Console.WriteLine($"  {spec.Name}");
            foreach (var engine in options.Engines)
            {
                var resultPath = Path.Combine(workDirectory, $"{spec.Name}-{engine}.json");
                var worker = new WorkerOptions(
                    databasePath,
                    spec.Name,
                    engine,
                    spec.Dimensions,
                    spec.VectorsPerItem,
                    options.Queries,
                    options.K,
                    options.Budget,
                    resultPath);
                if (RunWorker(worker) != 0 || !File.Exists(resultPath))
                {
                    Console.Error.WriteLine($"    {engine} failed; see the output above.");
                    continue;
                }

                results.Add(JsonSerializer.Deserialize<WorkerResult>(File.ReadAllText(resultPath), BenchmarkJson.Options)!);
            }

            libraries.Add(new LibraryReport(spec, new FileInfo(databasePath).Length, info, results));
        }

        var report = ReportWriter.Write(options, libraries);
        var reportPath = Path.Combine(options.OutputDirectory, $"vector-search-{stamp}.md");
        File.WriteAllText(reportPath, report);
        Console.WriteLine();
        Console.WriteLine(report);
        Console.WriteLine($"Report: {reportPath}");
        Console.WriteLine($"Raw results: {workDirectory}");
        return 0;
    }

    private static int RunWorker(WorkerOptions worker)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown process path.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };

        // Under "dotnet MediaFileClassifier.Benchmarks.dll" the process is dotnet itself.
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add(typeof(BenchmarkRunner).Assembly.Location);
        }

        foreach (var argument in worker.ToArguments())
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("The worker did not start.");
        process.WaitForExit();
        return process.ExitCode;
    }
}

/// <summary>
/// One library and what every engine measured on it.
/// </summary>
internal sealed record LibraryReport(
    LibrarySpec Spec,
    long DatabaseBytes,
    IReadOnlyDictionary<string, string> Info,
    IReadOnlyList<WorkerResult> Results);
