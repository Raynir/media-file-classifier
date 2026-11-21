using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Logging;
using MediaFileClassifier.Desktop.Diagnostics;
using MediaFileClassifier.Desktop.Platform;
using MediaFileClassifier.Infrastructure;
using MediaFileClassifier.Infrastructure.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.DataAccess;
using MediaFileClassifier.Infrastructure.Jobs;
using MediaFileClassifier.Infrastructure.Media;
using MediaFileClassifier.Infrastructure.Paths;
using MediaFileClassifier.Infrastructure.Playback;
using MediaFileClassifier.Infrastructure.Search;
using MediaFileClassifier.Ui;
using MediaFileClassifier.UseCases;
using MediaFileClassifier.UseCases.Embeddings;
using MediaFileClassifier.UseCases.Libraries.CheckFolderAvailability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace MediaFileClassifier.Desktop;

/// <summary>
/// The desktop entry point and composition root.
/// </summary>
internal static partial class Program
{
    private const string LogOutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    [STAThread]
    public static int Main(string[] args)
    {
        var appPaths = new AppPaths();
        int? smokeExitCode = null;
        StartSmoke(args, ref appPaths, ref smokeExitCode);
        if (smokeExitCode is { } usageError)
        {
            return usageError;
        }

        var logConfiguration = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(appPaths.LogsDirectory, "mfc-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: LogOutputTemplate);
        AddSmokeLogSink(logConfiguration);
        Log.Logger = logConfiguration.CreateLogger();

        try
        {
            var builder = Host.CreateApplicationBuilder(args);
            builder.Services
                .AddSerilog()
                .AddSingleton<IAppPaths>(appPaths)
                .AddDesktopPlatform()
                .AddInfrastructure()
                .AddAi()
                .AddDataAccess()
                .AddJobs()
                .AddMedia()
                .AddPlayback()
                .AddSearch()
                .AddUseCases()
                .AddUi()
                .Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);

            using var host = builder.Build();
            var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger(typeof(Program).FullName!);
            StartupInfo.Log(logger);
            StartupInfo.LogHardware(logger, host.Services.GetRequiredService<IHardwareInfo>());
            GlobalExceptionHandlers.Install(logger);
            Logger.Sink = new AvaloniaLogSink(loggerFactory);
            host.Services.GetRequiredService<ICapabilityService>().RefreshAsync().GetAwaiter().GetResult();

            // The model catalogue is checked at startup, so a malformed one fails here, not at a user's download (E06-S12).
            host.Services.GetRequiredService<IModelCatalog>();

            // The user's OpenAI-compatible endpoints become providers at startup (E17-S02).
            host.Services.GetRequiredService<IAiEndpointService>();

            // The search index loads the open library's vectors in the background from its first use (D067).
            host.Services.GetRequiredService<ISearchIndex>();

            // Search switches to a new embedding model once enough items have its vectors (E06-S07-T05).
            host.Services.GetRequiredService<EmbeddingModelMigration>();

            // Folder statuses follow drives and shares coming and going (E14-S07-T04).
            host.Services.GetRequiredService<FolderAvailabilityMonitor>().Start();

            if (args.Contains("--benchmark"))
            {
                return RunBenchmark(host.Services, logger);
            }

            UiCommandLine.Apply(args, host.Services);

            host.Start();
            var exitCode = BuildAvaloniaApp(host.Services)
                .StartWithClassicDesktopLifetime(args, lifetime =>
                {
                    SimulateCrashIfRequested(lifetime, args);
                    host.Services.GetRequiredService<WindowsVolumeWatcher>().Attach(lifetime);
                    AttachSmoke(lifetime, host.Services);
                });
            host.StopAsync().GetAwaiter().GetResult();
            logger.LogInformation("Exited with code {ExitCode}", exitCode);
            return exitCode;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "The app failed to start or stopped unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
            FinishSmoke();
        }
    }

    /// <summary>
    /// With <c>--benchmark</c>, times the AI analyzers on this machine, prints a table, saves the results for the
    /// progress estimate, and exits without opening the window (E06-S08-T04).
    /// </summary>
    private static int RunBenchmark(IServiceProvider services, Microsoft.Extensions.Logging.ILogger logger)
    {
        AttachParentConsole();
        var progress = new Progress<string>(line =>
        {
            Console.WriteLine(line);
            logger.LogInformation("Benchmark: {Line}", line);
        });
        var report = services.GetRequiredService<IBenchmarkRunner>().RunAsync(progress, CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine($"Machine: {report.Machine}");
        Console.WriteLine($"{"Analyzer",-14}{"Tier",-10}{"Provider",-10}{"Items/s",8}");
        foreach (var entry in report.Entries)
        {
            Console.WriteLine($"{entry.Analyzer,-14}{entry.TierId,-10}{entry.Provider,-10}{entry.ItemsPerSecond,8:0.0}");
        }

        return report.Entries.Count == 0 ? 2 : 0;
    }

    /// <summary>
    /// The app is a Windows GUI program, so it has no console of its own: attach to the one that started it, so
    /// <c>--benchmark</c> can print.
    /// </summary>
    private const int StdOutputHandle = -11;

    private static void AttachParentConsole()
    {
        // Redirected output (a pipe or a file) already has a handle; attaching would replace it with the console.
        if (NativeMethods.GetStdHandle(StdOutputHandle) == 0 && NativeMethods.AttachConsole(uint.MaxValue))
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    /// <summary>
    /// Avalonia configuration used by the XAML designer.
    /// </summary>
    /// <returns>The configured app builder.</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();

    private static AppBuilder BuildAvaloniaApp(IServiceProvider services)
        => AppBuilder.Configure(() => new App(services))
            .UsePlatformDetect()
            .WithInterFont();

    // The UI smoke tool (MFC-1006) implements these in Debug builds only, in Smoke/Program.Smoke.cs.
    static partial void StartSmoke(string[] args, ref AppPaths appPaths, ref int? exitCode);

    static partial void AddSmokeLogSink(LoggerConfiguration configuration);

    static partial void AttachSmoke(IClassicDesktopStyleApplicationLifetime lifetime, IServiceProvider services);

    static partial void FinishSmoke();

    /// <summary>
    /// With <c>--simulate-crash</c> in a Debug build, throws on the UI thread once the window is up,
    /// to check that unhandled exceptions are logged and don't close the app (E01-S01-T05).
    /// </summary>
    private static void SimulateCrashIfRequested(IClassicDesktopStyleApplicationLifetime lifetime, string[] args)
    {
#if DEBUG
        if (args.Contains("--simulate-crash"))
        {
            lifetime.Startup += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(
                () => throw new InvalidOperationException("Simulated crash (--simulate-crash)."),
                Avalonia.Threading.DispatcherPriority.Background);
        }
#endif
    }
}
