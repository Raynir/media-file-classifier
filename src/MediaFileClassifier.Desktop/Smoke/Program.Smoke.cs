#if DEBUG
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using MediaFileClassifier.Desktop.Smoke;
using MediaFileClassifier.Infrastructure.Paths;
using Serilog;

namespace MediaFileClassifier.Desktop;

/// <summary>
/// The UI smoke tool (MFC-1006), in Debug builds only: with <c>--smoke &lt;script&gt;</c>, the app starts on its own
/// app-data folder, runs the script's steps against the main window, saves the screenshots it asks for, writes a
/// report and exits with 0 when every step passed and nothing failing was logged, 1 when not, 2 for a bad script.
/// </summary>
internal static partial class Program
{
    private static SmokeOptions? smokeOptions;
    private static IReadOnlyList<SmokeStep>? smokeSteps;
    private static SmokeLogWatch? smokeLog;
    private static int smokeReported;

    static partial void StartSmoke(string[] args, ref AppPaths appPaths, ref int? exitCode)
    {
        try
        {
            smokeOptions = SmokeOptions.FromArgs(args);
            if (smokeOptions is null)
            {
                return;
            }

            smokeSteps = SmokeScript.Parse(File.ReadAllLines(smokeOptions.ScriptPath));
            if (smokeOptions.SampleMediaDirectory is null && smokeSteps.Any(step => step is { Verb: "library", Argument: "sample" }))
            {
                throw new ArgumentException("'library sample' needs the repository's tests/SampleMedia, which wasn't found above the script or the app.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or IOException or UnauthorizedAccessException)
        {
            AttachParentConsole();
            Console.Error.WriteLine($"Smoke: {ex.Message}");
            exitCode = 2;
            return;
        }

        Directory.CreateDirectory(smokeOptions.DataDirectory);
        appPaths = new AppPaths(smokeOptions.DataDirectory);
        smokeLog = new SmokeLogWatch();
    }

    static partial void AddSmokeLogSink(LoggerConfiguration configuration)
    {
        if (smokeLog is not null)
        {
            configuration.WriteTo.Sink(smokeLog);
        }
    }

    static partial void AttachSmoke(IClassicDesktopStyleApplicationLifetime lifetime, IServiceProvider services)
    {
        if (smokeOptions is null || smokeSteps is null || smokeLog is null)
        {
            return;
        }

        lifetime.Startup += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            var exitCode = 1;
            try
            {
                exitCode = await RunSmokeAsync(lifetime, services, smokeOptions, smokeSteps, smokeLog);
            }
#pragma warning disable CA1031 // Writing the report failed: log it, and still exit.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Log.Error(ex, "The smoke report couldn't be written");
            }
            finally
            {
                lifetime.Shutdown(exitCode);
            }
        });
    }

    /// <summary>
    /// Adds what failed after the report, such as an exception while the app shut down, which also makes the exit
    /// code 1.
    /// </summary>
    static partial void FinishSmoke()
    {
        if (smokeOptions is null || smokeLog is null || smokeLog.Failures.Count <= smokeReported)
        {
            return;
        }

        Directory.CreateDirectory(smokeOptions.OutputDirectory);
        string[] late = ["FAILED after the report, while the app shut down:", .. smokeLog.Failures.Skip(smokeReported).Select(line => "LOG   " + line)];
        File.AppendAllLines(Path.Combine(smokeOptions.OutputDirectory, "smoke-report.txt"), late);
        foreach (var line in late)
        {
            Console.WriteLine(line);
        }
    }

    private static async Task<int> RunSmokeAsync(
        IClassicDesktopStyleApplicationLifetime lifetime,
        IServiceProvider services,
        SmokeOptions options,
        IReadOnlyList<SmokeStep> steps,
        SmokeLogWatch log)
    {
        var report = new List<string> { $"Smoke script: {options.ScriptPath}", $"App data: {options.DataDirectory}" };
        string? failure;
        try
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            while (lifetime.MainWindow is not { IsVisible: true })
            {
                if (started.Elapsed > SmokeScript.DefaultWaitTimeout)
                {
                    throw new TimeoutException("the main window didn't show");
                }

                await Task.Delay(100);
            }

            var runner = new SmokeRunner(services, lifetime.MainWindow, options, log);
            try
            {
                failure = await runner.RunAsync(steps);
            }
            finally
            {
                report.AddRange(runner.Report);
            }
        }
#pragma warning disable CA1031 // Any exception fails the run and is reported, rather than crashing the app.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            failure = $"unexpected {ex}";
        }

        var logFailures = log.Failures;
        smokeReported = logFailures.Count;
        report.AddRange(logFailures.Select(line => "LOG   " + line));
        var passed = failure is null && logFailures.Count == 0;
        report.Add(passed
            ? "PASSED"
            : $"FAILED: {failure ?? $"{logFailures.Count} error(s) or Avalonia warning(s) were logged"}");
        report.Add($"Screenshots: {options.OutputDirectory}");
        report.Add($"Log: {Path.Combine(options.DataDirectory, "logs")}");

        Directory.CreateDirectory(options.OutputDirectory);
        await File.WriteAllLinesAsync(Path.Combine(options.OutputDirectory, "smoke-report.txt"), report);
        AttachParentConsole();
        foreach (var line in report)
        {
            Console.WriteLine(line);
        }

        Log.Information("Smoke run {Result}", passed ? "passed" : "failed");
        return passed ? 0 : 1;
    }
}
#endif
