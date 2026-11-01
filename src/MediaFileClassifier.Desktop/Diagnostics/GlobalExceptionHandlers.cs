using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Desktop.Diagnostics;

/// <summary>
/// Logs exceptions nothing else caught, so no failure is silent.
/// </summary>
internal static class GlobalExceptionHandlers
{
    /// <summary>
    /// Subscribes to the process, task scheduler and UI thread exception events.
    /// </summary>
    /// <param name="logger">Logger to write to.</param>
    public static void Install(ILogger logger)
    {
        // A crash on a background thread can't be prevented; log it before the process ends.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception, terminating: {IsTerminating}", e.IsTerminating);
            Serilog.Log.CloseAndFlush();
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.LogError(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        // UI thread exceptions are logged and the app keeps running.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            logger.LogError(e.Exception, "Unhandled exception on the UI thread");
            e.Handled = true;
        };
    }
}
