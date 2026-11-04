using MediaFileClassifier.Infrastructure.Abstractions.Settings;

namespace MediaFileClassifier.UseCases.Settings;

/// <summary>
/// Every setting the features registered with <see cref="SettingsRegistration.AddSetting"/> (E17-S01-T01).
/// </summary>
public sealed class SettingRegistry
{
    private readonly Dictionary<string, SettingDefinition> byKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingRegistry"/> class.
    /// </summary>
    /// <param name="settings">The registered settings.</param>
    /// <exception cref="InvalidOperationException">Two settings share a key, or a setting has an unknown category.</exception>
    public SettingRegistry(IEnumerable<SettingDefinition> settings)
    {
        byKey = new Dictionary<string, SettingDefinition>(StringComparer.Ordinal);
        foreach (var setting in settings.Distinct())
        {
            if (!SettingCategories.All.Contains(setting.Category))
            {
                throw new InvalidOperationException($"Setting {setting.Key} has an unknown category {setting.Category}.");
            }

            if (!byKey.TryAdd(setting.Key, setting))
            {
                throw new InvalidOperationException($"Two settings have the key {setting.Key}.");
            }
        }

        All = [.. byKey.Values
            .OrderBy(setting => IndexOf(setting.Category))
            .ThenBy(setting => setting.Order)
            .ThenBy(setting => setting.Key, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Gets every setting, in category order and then by <see cref="SettingDefinition.Order"/>.
    /// </summary>
    public IReadOnlyList<SettingDefinition> All { get; }

    /// <summary>
    /// Gets the setting with a key.
    /// </summary>
    /// <param name="key">The setting key.</param>
    /// <returns>The setting, or <c>null</c> if none has the key.</returns>
    public SettingDefinition? Find(string key) => byKey.GetValueOrDefault(key);

    /// <summary>
    /// Gets the settings of a category, in order.
    /// </summary>
    /// <param name="category">One of <see cref="SettingCategories"/>.</param>
    /// <returns>The settings.</returns>
    public IReadOnlyList<SettingDefinition> InCategory(string category)
        => [.. All.Where(setting => setting.Category == category)];

    private static int IndexOf(string category)
    {
        for (var i = 0; i < SettingCategories.All.Count; i++)
        {
            if (SettingCategories.All[i] == category)
            {
                return i;
            }
        }

        return int.MaxValue;
    }
}
