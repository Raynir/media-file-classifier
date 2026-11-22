using System.Collections.Concurrent;
using MediaFileClassifier.Ui.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Ui.Processing;

/// <summary>
/// Names a job's stage for the processing queue: the <c>Stage_&lt;key&gt;</c> string. A stage with no string is a gap in
/// the resources: the caller shows something else, and the gap is logged once per stage.
/// </summary>
public static partial class StageNames
{
    private static readonly ConcurrentDictionary<string, bool> Reported = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the logger for missing strings. The composition root sets it; until then nothing is logged.
    /// </summary>
    public static ILogger Logger { get; set; } = NullLogger.Instance;

    /// <summary>
    /// Gets the name of a stage.
    /// </summary>
    /// <param name="localizer">UI strings.</param>
    /// <param name="stage">The stage key.</param>
    /// <returns>The name, or <see langword="null"/> when there is no <c>Stage_</c> string for it.</returns>
    public static string? Find(Localizer localizer, string stage)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        var name = localizer.Find("Stage_" + stage);
        if (name is null && Reported.TryAdd(stage, true))
        {
            LogMissing(Logger, "Stage_" + stage);
        }

        return name;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No string for the processing stage; add {Key} to Strings.resx")]
    private static partial void LogMissing(ILogger logger, string key);
}
