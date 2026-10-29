using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Logging;
using MediaFileClassifier.Desktop.Diagnostics;
using MediaFileClassifier.Desktop.Platform;
using MediaFileClassifier.Infrastructure;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.Paths;
using MediaFileClassifier.Ui;
using MediaFileClassifier.UseCases;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace MediaFileClassifier.Desktop;

/// <summary>
/// The desktop entry point and composition root.
/// </summary>
internal static class Program
{
    private const string LogOutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    [STAThread]
    public static int Main(string[] args)
    {
        var appPaths = new AppPaths();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(appPaths.LogsDirectory, "mfc-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: LogOutputTemplate)
            .CreateLogger();

        try
        {
            var builder = Host.CreateApplicationBuilder(args);
            builder.Services
                .AddSerilog()
                .AddSingleton<IAppPaths>(appPaths)
                .AddDesktopPlatform()
                .AddInfrastructure()
                .AddUseCases()
                .AddUi()
                .Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);

            using var host = builder.Build();
            var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger(typeof(Program).FullName!);
            StartupInfo.Log(logger);
            GlobalExceptionHandlers.Install(logger);
            Logger.Sink = new AvaloniaLogSink(loggerFactory);

            host.Start();
            var exitCode = BuildAvaloniaApp(host.Services)
                .StartWithClassicDesktopLifetime(args, lifetime => SimulateCrashIfRequested(lifetime, args));
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
