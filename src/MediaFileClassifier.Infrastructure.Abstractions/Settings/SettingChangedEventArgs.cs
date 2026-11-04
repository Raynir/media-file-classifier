namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// Data of <see cref="ISettingsStore.Changed"/>.
/// </summary>
/// <param name="setting">The setting whose value changed.</param>
public sealed class SettingChangedEventArgs(SettingDefinition setting) : EventArgs
{
    /// <summary>
    /// Gets the setting whose value changed.
    /// </summary>
    public SettingDefinition Setting { get; } = setting;
}
