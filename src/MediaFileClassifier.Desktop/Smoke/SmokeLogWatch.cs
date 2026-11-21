#if DEBUG
using Serilog.Core;
using Serilog.Events;

namespace MediaFileClassifier.Desktop.Smoke;

/// <summary>
/// Collects the log entries that fail a smoke run (MFC-1006): any error, and any warning from Avalonia, such as a
/// binding warning. The app's own warnings don't fail a run.
/// </summary>
internal sealed class SmokeLogWatch : ILogEventSink
{
    private readonly Lock gate = new();
    private readonly List<string> failures = [];

    /// <summary>
    /// Gets or sets the step that is running, to name it next to each failure.
    /// </summary>
    public string CurrentStep { get; set; } = "startup";

    private readonly HashSet<string> allowedErrorSources = new(StringComparer.Ordinal);

    /// <summary>
    /// Lets a logger's errors pass, for a script that expects them (<c>allow-error</c>), e.g. after
    /// <c>damage-library</c>. Fatal entries still fail the run.
    /// </summary>
    /// <param name="source">The logger name, e.g. a handler's full type name.</param>
    public void AllowErrors(string source)
    {
        lock (gate)
        {
            allowedErrorSources.Add(source);
        }
    }

    /// <summary>
    /// Gets the failures so far, as report lines.
    /// </summary>
    public IReadOnlyList<string> Failures
    {
        get
        {
            lock (gate)
            {
                return [.. failures];
            }
        }
    }

    /// <summary>
    /// Tells whether an entry fails the run.
    /// </summary>
    /// <param name="level">The entry's level.</param>
    /// <param name="sourceContext">The entry's logger name, or <see langword="null"/>.</param>
    /// <returns>Whether it fails the run.</returns>
    public static bool Fails(LogEventLevel level, string? sourceContext)
        => level >= LogEventLevel.Error
            || (level == LogEventLevel.Warning && sourceContext?.StartsWith("Avalonia", StringComparison.Ordinal) == true);

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        var source = logEvent.Properties.TryGetValue("SourceContext", out var value) && value is ScalarValue { Value: string text }
            ? text
            : null;
        if (!Fails(logEvent.Level, source))
        {
            return;
        }

        var line = $"[{logEvent.Level}] {source}: {logEvent.RenderMessage()} (during '{CurrentStep}')";
        if (logEvent.Exception is { } exception)
        {
            line += $" {exception.GetType().Name}: {exception.Message}";
        }

        lock (gate)
        {
            if (logEvent.Level == LogEventLevel.Error && source is not null && allowedErrorSources.Contains(source))
            {
                return;
            }

            failures.Add(line);
        }
    }
}
#endif
