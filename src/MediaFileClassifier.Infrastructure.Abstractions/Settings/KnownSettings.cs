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
}
