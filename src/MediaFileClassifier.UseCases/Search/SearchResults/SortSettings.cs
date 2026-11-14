using MediaFileClassifier.Infrastructure.Abstractions.Settings;

namespace MediaFileClassifier.UseCases.Search.SearchResults;

/// <summary>
/// The remembered orders (E10-S04-T02): app settings, so every library opens in the order the user chose last. They
/// aren't registered, so the Settings window doesn't show them; the sort control changes them.
/// </summary>
public static class SortSettings
{
    /// <summary>
    /// The order while searching, as <see cref="SortChoice.ToString"/> writes it.
    /// </summary>
    public static readonly TextSetting SearchSort = new(
        "search.sort", SettingScope.App, SettingCategories.General, "Setting_SearchSort", SortChoice.SearchDefault.ToString());

    /// <summary>
    /// The order while browsing, as <see cref="SortChoice.ToString"/> writes it.
    /// </summary>
    public static readonly TextSetting BrowseSort = new(
        "browse.sort", SettingScope.App, SettingCategories.General, "Setting_BrowseSort", SortChoice.BrowseDefault.ToString());
}
