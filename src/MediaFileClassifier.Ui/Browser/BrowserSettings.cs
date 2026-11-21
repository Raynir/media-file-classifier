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
}
