using System.Collections;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// Stored values of registered settings, keyed by <see cref="SettingDefinition.Key"/>. Immutable, with value
/// equality. Keys that no registered setting uses are kept, so a newer version's settings survive a downgrade.
/// </summary>
[JsonConverter(typeof(SettingValuesJsonConverter))]
public sealed class SettingValues : IReadOnlyDictionary<string, JsonElement>, IEquatable<SettingValues>
{
    private readonly ImmutableSortedDictionary<string, JsonElement> values;

    private SettingValues(ImmutableSortedDictionary<string, JsonElement> values)
    {
        this.values = values;
    }

    /// <summary>
    /// Gets the empty set of values.
    /// </summary>
    public static SettingValues Empty { get; } = new(ImmutableSortedDictionary.Create<string, JsonElement>(StringComparer.Ordinal));

    /// <inheritdoc />
    public int Count => values.Count;

    /// <inheritdoc />
    public IEnumerable<string> Keys => values.Keys;

    /// <inheritdoc />
    IEnumerable<JsonElement> IReadOnlyDictionary<string, JsonElement>.Values => values.Values;

    /// <inheritdoc />
    public JsonElement this[string key] => values[key];

    /// <summary>
    /// Returns a copy with a value stored.
    /// </summary>
    /// <param name="key">The setting key.</param>
    /// <param name="value">The JSON value.</param>
    /// <returns>The new set of values.</returns>
    public SettingValues With(string key, JsonElement value) => new(values.SetItem(key, value.Clone()));

    /// <summary>
    /// Returns a copy without a value.
    /// </summary>
    /// <param name="key">The setting key.</param>
    /// <returns>The new set of values.</returns>
    public SettingValues Without(string key) => values.ContainsKey(key) ? new(values.Remove(key)) : this;

    /// <inheritdoc />
    public bool ContainsKey(string key) => values.ContainsKey(key);

    /// <inheritdoc />
    public bool TryGetValue(string key, out JsonElement value) => values.TryGetValue(key, out value);

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, JsonElement>> GetEnumerator() => values.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public bool Equals(SettingValues? other)
        => other is not null
            && other.Count == Count
            && values.All(pair => other.TryGetValue(pair.Key, out var value) && JsonElement.DeepEquals(pair.Value, value));

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SettingValues);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var key in values.Keys)
        {
            hash.Add(key, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Reads and writes <see cref="SettingValues"/> as a JSON object. Public only for the JSON source generator.
    /// </summary>
    public sealed class SettingValuesJsonConverter : JsonConverter<SettingValues>
    {
        /// <inheritdoc />
        public override SettingValues Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Setting values must be a JSON object.");
            }

            var result = Empty;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                result = result.With(property.Name, property.Value);
            }

            return result;
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, SettingValues value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var (key, element) in value)
            {
                writer.WritePropertyName(key);
                element.WriteTo(writer);
            }

            writer.WriteEndObject();
        }
    }
}
