using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// A whole-number setting within a range, shown as a number box.
/// </summary>
public sealed class IntegerSetting : SettingDefinition<int>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IntegerSetting"/> class.
    /// </summary>
    /// <param name="key">The setting key.</param>
    /// <param name="scope">Where the value is stored.</param>
    /// <param name="category">One of <see cref="SettingCategories"/>.</param>
    /// <param name="labelKey">Resource key of the label.</param>
    /// <param name="defaultValue">The default value.</param>
    /// <param name="minimum">The smallest allowed value.</param>
    /// <param name="maximum">The largest allowed value.</param>
    public IntegerSetting(string key, SettingScope scope, string category, string labelKey, int defaultValue, int minimum, int maximum)
        : base(key, scope, category, labelKey, defaultValue)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimum, maximum);
        ArgumentOutOfRangeException.ThrowIfLessThan(defaultValue, minimum);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(defaultValue, maximum);
        Minimum = minimum;
        Maximum = maximum;
    }

    /// <summary>
    /// Gets the smallest allowed value.
    /// </summary>
    public int Minimum { get; }

    /// <summary>
    /// Gets the largest allowed value.
    /// </summary>
    public int Maximum { get; }

    private protected override SettingValidationError? ValidateCore(int value)
        => value < Minimum || value > Maximum ? new SettingValidationError("SettingError_Range", Minimum, Maximum) : null;

    private protected override void Write(Utf8JsonWriter writer, int value) => writer.WriteNumberValue(value);

    private protected override bool TryRead(JsonElement json, [NotNullWhen(true)] out int value)
    {
        value = 0;
        return json.ValueKind == JsonValueKind.Number && json.TryGetInt32(out value);
    }
}
