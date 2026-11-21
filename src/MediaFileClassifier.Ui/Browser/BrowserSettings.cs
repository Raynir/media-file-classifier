using MediaFileClassifier.Infrastructure.Abstractions.Settings;

namespace MediaFileClassifier.Ui.Browser;

/// <summary>
/// The appearance settings of the media browser.
/// </summary>
public static class BrowserSettings
{
    /// <summary>
    /// Whether tiles show the file name under the thumbnail (E14-S01, E17-S05). The type and size line stays either way.
    /// </summary>
    public static readonly BooleanSetting ShowFileNames = new(
        "appearance.showFileNames", SettingScope.App, SettingCategories.Appearance, "Setting_ShowFileNames", defaultValue: true)
    {
        HelpKey = "Setting_ShowFileNames_Help",
        Order = 100,
    };

    /// <summary>
    /// The list view's column setup (E14-S02-T02), as <see cref="ViewModels.Browser.ListColumnsViewModel.Serialize"/>
    /// writes it; empty for the default setup. It isn't registered, so the Settings window doesn't show it; the list
    /// header changes it.
    /// </summary>
    public static readonly TextSetting ListColumns = new(
        "browse.listColumns", SettingScope.App, SettingCategories.Appearance, "Setting_ListColumns", string.Empty);
}
