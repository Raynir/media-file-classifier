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
    /// The default minutes between two quick syncs of a folder in Poll mode (E04-S05-T02).
    /// </summary>
    public const int DefaultPollIntervalMinutes = 15;

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

    /// <summary>
    /// The minutes between two quick syncs of each folder in Poll mode (E04-S05-T02): from 1 minute to a day. It's stored
    /// in <c>settings.json</c>, for every library.
    /// </summary>
    public static readonly IntegerSetting PollIntervalMinutes = new(
        "monitoring.pollIntervalMinutes", SettingScope.App, SettingCategories.General, "Setting_MonitoringPollInterval", DefaultPollIntervalMinutes, 1, 1440)
    {
        HelpKey = "Setting_MonitoringPollInterval_Help",
        Order = 310,
    };
}
