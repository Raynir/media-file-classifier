using System.Text.Json;

namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// Raw storage of the library-scope settings of the open library, keyed by <see cref="SettingDefinition.Key"/>.
/// <see cref="ISettingsStore"/> reads and writes library settings through it.
/// </summary>
public interface ILibrarySettingValues
{
    /// <summary>
    /// Raised when <see cref="IsAvailable"/> changes.
    /// </summary>
    event EventHandler? AvailabilityChanged;

    /// <summary>
    /// Gets a value indicating whether a library is open, so its settings can be read and written.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Gets the stored value of a setting.
    /// </summary>
    /// <param name="key">The setting key.</param>
    /// <param name="value">The stored value.</param>
    /// <returns><c>true</c> if a value is stored.</returns>
    bool TryGet(string key, out JsonElement value);

    /// <summary>
    /// Stores and removes values in one write.
    /// </summary>
    /// <param name="changes">New values by key; <c>null</c> removes the stored value.</param>
    void Apply(IReadOnlyDictionary<string, JsonElement?> changes);
}
