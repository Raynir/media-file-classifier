using MediaFileClassifier.Infrastructure.Abstractions.Settings;

namespace MediaFileClassifier.UseCases.Search.Filters;

/// <summary>
/// The settings of the filter state that survives a restart (E10-S02-T03).
/// </summary>
public static class FilterSettings
{
    /// <summary>
    /// Whether the filters in force when the library was last used come back when it opens.
    /// </summary>
    public static readonly BooleanSetting RestoreOnStart = new(
        "search.restoreFilters", SettingScope.App, SettingCategories.General, "Setting_RestoreFilters", defaultValue: false)
    {
        HelpKey = "Setting_RestoreFilters_Help",
        Order = 110,
    };

    /// <summary>
    /// The filters in force when the library was last used, as <see cref="FilterStateSerializer"/> JSON. Kept per
    /// library, because folders and tags belong to one. It isn't registered, so the settings window doesn't list it.
    /// The library stores it, so it's defined with the other library settings.
    /// </summary>
    public static readonly TextSetting LastFilters = KnownSettings.LastFilters;
}
