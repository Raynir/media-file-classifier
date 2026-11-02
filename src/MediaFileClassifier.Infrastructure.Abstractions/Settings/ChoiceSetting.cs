using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// A setting whose value is one member of an enum, shown as a drop-down list. The value is stored as the
/// member's camel-case name, so reordering members doesn't change stored values.
/// </summary>
/// <typeparam name="TEnum">The enum.</typeparam>
public sealed class ChoiceSetting<TEnum> : SettingDefinition<TEnum>, IChoiceSetting
    where TEnum : struct, Enum
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChoiceSetting{TEnum}"/> class.
    /// </summary>
    /// <param name="key">The setting key.</param>
    /// <param name="scope">Where the value is stored.</param>
    /// <param name="category">One of <see cref="SettingCategories"/>.</param>
    /// <param name="labelKey">Resource key of the label.</param>
    /// <param name="defaultValue">The default value.</param>
    public ChoiceSetting(string key, SettingScope scope, string category, string labelKey, TEnum defaultValue)
        : base(key, scope, category, labelKey, defaultValue)
    {
        Options = Enum.GetValues<TEnum>();
    }

    /// <summary>
    /// Gets the options in the order the enum declares them.
    /// </summary>
    public IReadOnlyList<TEnum> Options { get; }

    /// <inheritdoc />
    IReadOnlyList<object> IChoiceSetting.Options => [.. Options.Select(option => (object)option)];

    /// <inheritdoc />
    public string OptionLabelKey(object option) => $"{LabelKey}_{option}";

    private protected override SettingValidationError? ValidateCore(TEnum value)
        => Enum.IsDefined(value) ? null : new SettingValidationError("SettingError_UnknownOption");

    private protected override void Write(Utf8JsonWriter writer, TEnum value)
        => writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));

    private protected override bool TryRead(JsonElement json, [NotNullWhen(true)] out TEnum value)
    {
        value = default;
        var name = json.ValueKind == JsonValueKind.String ? json.GetString() : null;

        // Enum.TryParse also accepts numbers and comma lists, which aren't stored forms.
        return !string.IsNullOrEmpty(name)
            && char.IsLetter(name[0])
            && !name.Contains(',', StringComparison.Ordinal)
            && Enum.TryParse(name, ignoreCase: true, out value)
            && Enum.IsDefined(value);
    }
}

/// <summary>
/// The parts of <see cref="ChoiceSetting{TEnum}"/> that don't depend on the enum, for building its control.
/// </summary>
public interface IChoiceSetting
{
    /// <summary>
    /// Gets the options, boxed, in order.
    /// </summary>
    IReadOnlyList<object> Options { get; }

    /// <summary>
    /// Gets the resource key of an option's label: the setting's label key, an underscore and the member name.
    /// </summary>
    /// <param name="option">One of <see cref="Options"/>.</param>
    /// <returns>The resource key.</returns>
    string OptionLabelKey(object option);
}
