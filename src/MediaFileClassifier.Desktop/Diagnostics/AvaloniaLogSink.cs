using Avalonia.Logging;
using Microsoft.Extensions.Logging;
using AvaloniaLogLevel = Avalonia.Logging.LogEventLevel;

namespace MediaFileClassifier.Desktop.Diagnostics;

/// <summary>
/// Forwards Avalonia's internal log to <see cref="ILogger"/>, so it lands in the app log file.
/// Only warnings and above are forwarded; Avalonia's verbose output isn't useful in the file.
/// </summary>
internal sealed class AvaloniaLogSink : ILogSink
{
    private readonly ILoggerFactory loggerFactory;

    public AvaloniaLogSink(ILoggerFactory loggerFactory)
    {
        this.loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public bool IsEnabled(AvaloniaLogLevel level, string area) => level >= AvaloniaLogLevel.Warning;

    /// <inheritdoc />
    public void Log(AvaloniaLogLevel level, string area, object? source, string messageTemplate)
        => Log(level, area, source, messageTemplate, []);

    /// <inheritdoc />
    public void Log(AvaloniaLogLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        if (!IsEnabled(level, area))
        {
            return;
        }

        var logger = loggerFactory.CreateLogger($"Avalonia.{area}");
#pragma warning disable CA2254 // The template comes from Avalonia.
        logger.Log(ToLogLevel(level), messageTemplate, propertyValues);
#pragma warning restore CA2254
    }

    private static LogLevel ToLogLevel(AvaloniaLogLevel level) => level switch
    {
        AvaloniaLogLevel.Verbose => LogLevel.Trace,
        AvaloniaLogLevel.Debug => LogLevel.Debug,
        AvaloniaLogLevel.Information => LogLevel.Information,
        AvaloniaLogLevel.Warning => LogLevel.Warning,
        AvaloniaLogLevel.Error => LogLevel.Error,
        _ => LogLevel.Critical,
    };
}
