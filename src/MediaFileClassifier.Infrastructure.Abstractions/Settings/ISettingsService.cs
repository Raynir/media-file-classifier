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
}
