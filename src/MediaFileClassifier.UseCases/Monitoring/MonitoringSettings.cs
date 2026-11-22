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
    /// Whether opening a library runs a quick sync of its Online folders (E04-S03-T01): it picks up
    /// the changes made while the app was closed. Off for very large or slow drives. It's stored in the library.
    /// </summary>
    public static readonly BooleanSetting QuickSyncOnOpen = new(
        "monitoring.quickSyncOnOpen", SettingScope.Library, SettingCategories.Library, "Setting_QuickSyncOnOpen", defaultValue: true)
    {
        HelpKey = "Setting_QuickSyncOnOpen_Help",
        Order = 300,
    };
}
