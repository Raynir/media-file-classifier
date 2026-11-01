namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// Reads and writes the values of registered settings (E17-S01). A setting without a stored value has its default.
/// App settings are always available; library settings only while a library is open.
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// Raised after a setting's value changed, on the thread that changed it.
    /// </summary>
    event EventHandler<SettingChangedEventArgs>? Changed;

    /// <summary>
    /// Raised when library settings become available or unavailable, as a library opens or closes.
    /// </summary>
    event EventHandler? AvailabilityChanged;

    /// <summary>
    /// Gets a value indicating whether settings of a scope can be read from and written to their storage.
    /// </summary>
    /// <param name="scope">The scope.</param>
    /// <returns><c>true</c> if they are available.</returns>
    bool IsAvailable(SettingScope scope);

    /// <summary>
    /// Gets a setting's value.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="setting">The setting.</param>
    /// <returns>The stored value, or the default when nothing valid is stored or the scope is unavailable.</returns>
    T Get<T>(SettingDefinition<T> setting)
        where T : notnull;

    /// <summary>
    /// Gets a setting's value, boxed.
    /// </summary>
    /// <param name="setting">The setting.</param>
    /// <returns>The stored value, or the default when nothing valid is stored or the scope is unavailable.</returns>
    object GetValue(SettingDefinition setting);

    /// <summary>
    /// Stores a setting's value.
    /// </summary>
    /// <param name="setting">The setting.</param>
    /// <param name="value">The new value, of the setting's <see cref="SettingDefinition.ValueType"/>.</param>
    /// <exception cref="ArgumentException">The value is of the wrong type or isn't valid.</exception>
    /// <exception cref="InvalidOperationException">The setting's scope is unavailable.</exception>
    void SetValue(SettingDefinition setting, object value);

    /// <summary>
    /// Removes the stored values of settings, so they have their defaults again. Settings whose scope is
    /// unavailable are skipped.
    /// </summary>
    /// <param name="settings">The settings.</param>
    void Reset(IEnumerable<SettingDefinition> settings);
}
