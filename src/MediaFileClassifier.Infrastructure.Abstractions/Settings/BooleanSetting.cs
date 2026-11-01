using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// An on/off setting, shown as a toggle switch.
/// </summary>
/// <param name="key">The setting key.</param>
/// <param name="scope">Where the value is stored.</param>
/// <param name="category">One of <see cref="SettingCategories"/>.</param>
/// <param name="labelKey">Resource key of the label.</param>
/// <param name="defaultValue">The default value.</param>
public sealed class BooleanSetting(string key, SettingScope scope, string category, string labelKey, bool defaultValue)
    : SettingDefinition<bool>(key, scope, category, labelKey, defaultValue)
{
    private protected override void Write(Utf8JsonWriter writer, bool value) => writer.WriteBooleanValue(value);

    private protected override bool TryRead(JsonElement json, [NotNullWhen(true)] out bool value)
    {
        value = json.ValueKind == JsonValueKind.True;
        return json.ValueKind is JsonValueKind.True or JsonValueKind.False;
    }
}
