namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// Settings of features that have no project of their own yet. Other Stories define their settings next to their
/// feature, register them with <c>AddSetting</c>, and add their strings to the UI resources.
/// </summary>
public static class KnownSettings
{
    /// <summary>
    /// Base URL of an OpenAI-compatible server such as LM Studio or Ollama; empty if none is configured (E06, E17-S02).
    /// </summary>
    public static readonly TextSetting OpenAiCompatibleEndpoint = new(
        "ai.openAiCompatibleEndpoint", SettingScope.App, SettingCategories.AiModels, "Setting_OpenAiCompatibleEndpoint", string.Empty)
    {
        HelpKey = "Setting_OpenAiCompatibleEndpoint_Help",
        PlaceholderKey = "Setting_OpenAiCompatibleEndpoint_Placeholder",
        Validator = TextSetting.EmptyOrHttpUrl,
    };

    /// <summary>
    /// Whether informational notifications show in the app (E15-S01, E17-S05). Errors and warnings always show.
    /// </summary>
    public static readonly BooleanSetting InformationalNotifications = new(
        "general.informationalNotifications", SettingScope.App, SettingCategories.General, "Setting_InformationalNotifications", defaultValue: true)
    {
        HelpKey = "Setting_InformationalNotifications_Help",
        Order = 120,
    };

    /// <summary>
    /// Whether new items are analysed automatically after import (spec 1.4). Mirrors
    /// <c>LibrarySettings.AnalyzeAutomatically</c>.
    /// </summary>
    public static readonly BooleanSetting AnalyzeAutomatically = new(
        "library.analyzeAutomatically", SettingScope.Library, SettingCategories.Library, "Setting_AnalyzeAutomatically", defaultValue: true)
    {
        HelpKey = "Setting_AnalyzeAutomatically_Help",
        Order = 100,
    };

    /// <summary>
    /// How many previous analysis runs are kept per item and analyser (D020). Mirrors
    /// <c>LibrarySettings.RetainedPreviousRuns</c>.
    /// </summary>
    public static readonly IntegerSetting RetainedPreviousRuns = new(
        "library.retainedPreviousRuns", SettingScope.Library, SettingCategories.Library, "Setting_RetainedPreviousRuns", defaultValue: 1, minimum: 0, maximum: 10)
    {
        HelpKey = "Setting_RetainedPreviousRuns_Help",
        Order = 200,
    };

    /// <summary>
    /// Whether opening a library runs a quick sync of its Online folders (E04-S03-T01): it picks up the changes made
    /// while the app was closed. Off for very large or slow drives. It's stored in the library.
    /// </summary>
    public static readonly BooleanSetting QuickSyncOnOpen = new(
        "monitoring.quickSyncOnOpen", SettingScope.Library, SettingCategories.Library, "Setting_QuickSyncOnOpen", defaultValue: true)
    {
        HelpKey = "Setting_QuickSyncOnOpen_Help",
        Order = 300,
    };

    /// <summary>
    /// Whether the library's folders are monitored for changes (E04-S06-T01): turned off, no folder is watched, and the
    /// library only changes through rescans. It's stored in the library; each folder's own mode comes on top.
    /// </summary>
    public static readonly BooleanSetting MonitoringEnabled = new(
        "monitoring.enabled", SettingScope.Library, SettingCategories.Library, "Setting_MonitoringEnabled", defaultValue: true)
    {
        HelpKey = "Setting_MonitoringEnabled_Help",
        Order = 290,
    };

    /// <summary>
    /// The filters in force when the library was last used, as the search's filter-state JSON (D314). Mirrors
    /// <c>LibrarySettings.LastFilters</c>. It isn't registered, so the settings window doesn't list it.
    /// </summary>
    public static readonly TextSetting LastFilters = new(
        "search.lastFilters", SettingScope.Library, SettingCategories.Library, "Setting_LastFilters", defaultValue: string.Empty);
}
