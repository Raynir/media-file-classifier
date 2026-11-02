using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// A text setting, shown as a text box. An empty string means "not set".
/// </summary>
/// <param name="key">The setting key.</param>
/// <param name="scope">Where the value is stored.</param>
/// <param name="category">One of <see cref="SettingCategories"/>.</param>
/// <param name="labelKey">Resource key of the label.</param>
/// <param name="defaultValue">The default value.</param>
public sealed class TextSetting(string key, SettingScope scope, string category, string labelKey, string defaultValue)
    : SettingDefinition<string>(key, scope, category, labelKey, defaultValue)
{
    /// <summary>
    /// Gets the resource key of the text shown in the empty box, or <c>null</c> for none.
    /// </summary>
    public string? PlaceholderKey { get; init; }

    /// <summary>
    /// Checks that a value is empty or an absolute <c>http</c> or <c>https</c> URL.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns><c>null</c> if the value is valid, or why it isn't.</returns>
    public static SettingValidationError? EmptyOrHttpUrl(string value)
        => value.Length == 0
            || (Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            ? null
            : new SettingValidationError("SettingError_HttpUrl");

    private protected override void Write(Utf8JsonWriter writer, string value) => writer.WriteStringValue(value);

    private protected override bool TryRead(JsonElement json, [NotNullWhen(true)] out string? value)
    {
        value = json.ValueKind == JsonValueKind.String ? json.GetString() : null;
        return value is not null;
    }
}
