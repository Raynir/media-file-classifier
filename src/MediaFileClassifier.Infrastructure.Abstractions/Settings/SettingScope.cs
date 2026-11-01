namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// Where a setting is stored (E17).
/// </summary>
public enum SettingScope
{
    /// <summary>
    /// In the app-data <c>settings.json</c>, for every library on this device.
    /// </summary>
    App,

    /// <summary>
    /// In the open library, so it travels with the library to another device.
    /// </summary>
    Library,
}
