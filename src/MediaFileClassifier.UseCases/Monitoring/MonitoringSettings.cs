using MediaFileClassifier.Infrastructure.Abstractions.Settings;

namespace MediaFileClassifier.UseCases.Monitoring;

/// <summary>
/// Settings of folder monitoring (E04).
/// </summary>
public static class MonitoringSettings
{
    /// <summary>
    /// The default ignore list: temporary files, Office lock files, browser and download-manager partial downloads and
    /// editor swap and backup files.
    /// </summary>
    public const string DefaultIgnorePatterns =
        "*.tmp; *.temp; ~$*; *.crdownload; *.part; *.partial; *.download; *.opdownload; *.!ut; *.swp; *.swo; *.swx; *~; .~lock.*#; .#*";

    /// <summary>
    /// How many changes a monitored folder's batch needs before the app tells the user about it (E04-S06-T03,
    /// DNEW-3). Smaller batches are only logged. It's stored in <c>settings.json</c>, for every library.
    /// </summary>
    public static readonly IntegerSetting NotifyThreshold = new(
        "monitoring.notifyThreshold", SettingScope.App, SettingCategories.General, "Setting_MonitoringNotifyThreshold", 10, 1, 1000)
    {
        HelpKey = "Setting_MonitoringNotifyThreshold_Help",
        Order = 310,
    };

    /// <summary>
    /// The files folder watching ignores while they're written (E04-S01-T03, DNEW-2): globs separated by <c>;</c>, matched
    /// like a folder's exclude patterns. It's stored in <c>settings.json</c>, for every library.
    /// </summary>
    public static readonly TextSetting IgnorePatterns = new(
        "monitoring.ignorePatterns", SettingScope.App, SettingCategories.General, "Setting_MonitoringIgnorePatterns", DefaultIgnorePatterns)
    {
        HelpKey = "Setting_MonitoringIgnorePatterns_Help",
        Order = 300,
    };
}
