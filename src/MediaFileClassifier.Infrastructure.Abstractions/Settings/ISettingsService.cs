namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// Loads and saves the app settings.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// The current settings, loaded on first access. A missing or corrupt file gives the defaults.
    /// </summary>
    AppSettings Current { get; }

    /// <summary>
    /// Replaces the stored settings and <see cref="Current"/>.
    /// </summary>
    /// <param name="settings">Settings to store.</param>
    void Save(AppSettings settings);

    /// <summary>
    /// Changes the settings and stores the result, as one step that other writers can't interleave with.
    /// </summary>
    /// <param name="change">Makes the new settings from the current ones.</param>
    /// <returns>The stored settings.</returns>
    AppSettings Update(Func<AppSettings, AppSettings> change);
}
