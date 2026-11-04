using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// A setting that a feature registers (E17-S01): its key, value type, default, scope, category, texts,
/// validation and apply mode. The settings window builds a control for it from its type: see
/// <see cref="BooleanSetting"/>, <see cref="IntegerSetting"/>, <see cref="TextSetting"/> and
/// <see cref="ChoiceSetting{TEnum}"/>.
/// </summary>
public abstract class SettingDefinition
{
    private protected SettingDefinition(string key, SettingScope scope, string category, string labelKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(labelKey);
        Key = key;
        Scope = scope;
        Category = category;
        LabelKey = labelKey;
    }

    /// <summary>
    /// Gets the unique key the value is stored under, such as <c>appearance.theme</c>. Never change a released key.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets where the value is stored.
    /// </summary>
    public SettingScope Scope { get; }

    /// <summary>
    /// Gets the category, one of <see cref="SettingCategories"/>.
    /// </summary>
    public string Category { get; }

    /// <summary>
    /// Gets the resource key of the label.
    /// </summary>
    public string LabelKey { get; }

    /// <summary>
    /// Gets the resource key of the help text under the label, or <c>null</c> for none.
    /// </summary>
    public string? HelpKey { get; init; }

    /// <summary>
    /// Gets when a change takes effect.
    /// </summary>
    public SettingApplyMode ApplyMode { get; init; } = SettingApplyMode.Immediate;

    /// <summary>
    /// Gets the position within the category; lower comes first.
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// Gets the type of the value.
    /// </summary>
    public abstract Type ValueType { get; }

    /// <summary>
    /// Gets the default value, boxed.
    /// </summary>
    public abstract object DefaultValue { get; }

    /// <summary>
    /// Checks a boxed value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns><c>null</c> if the value is valid, or why it isn't.</returns>
    /// <exception cref="ArgumentException">The value isn't of <see cref="ValueType"/>.</exception>
    public abstract SettingValidationError? ValidateValue(object value);

    /// <summary>
    /// Converts a boxed value to its stored JSON form.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The JSON value.</returns>
    /// <exception cref="ArgumentException">The value isn't of <see cref="ValueType"/>.</exception>
    public abstract JsonElement Serialize(object value);

    /// <summary>
    /// Reads a stored JSON value. A value that doesn't parse or isn't valid is rejected.
    /// </summary>
    /// <param name="json">The stored JSON value.</param>
    /// <param name="value">The boxed value.</param>
    /// <returns><c>true</c> if the stored value is usable.</returns>
    public abstract bool TryDeserialize(JsonElement json, [NotNullWhen(true)] out object? value);

    /// <inheritdoc />
    public override string ToString() => Key;
}

/// <summary>
/// A setting with a value of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
public abstract class SettingDefinition<T> : SettingDefinition
    where T : notnull
{
    private protected SettingDefinition(string key, SettingScope scope, string category, string labelKey, T defaultValue)
        : base(key, scope, category, labelKey)
    {
        Default = defaultValue;
    }

    /// <summary>
    /// Gets the default value.
    /// </summary>
    public T Default { get; }

    /// <summary>
    /// Gets an extra check run after the type's own checks, or <c>null</c> for none.
    /// </summary>
    public Func<T, SettingValidationError?>? Validator { get; init; }

    /// <inheritdoc />
    public override Type ValueType => typeof(T);

    /// <inheritdoc />
    public override object DefaultValue => Default;

    /// <summary>
    /// Checks a value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns><c>null</c> if the value is valid, or why it isn't.</returns>
    public SettingValidationError? Validate(T value) => ValidateCore(value) ?? Validator?.Invoke(value);

    /// <inheritdoc />
    public override SettingValidationError? ValidateValue(object value) => Validate(Cast(value));

    /// <inheritdoc />
    public override JsonElement Serialize(object value)
    {
        var typed = Cast(value);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Write(writer, typed);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    /// <inheritdoc />
    public override bool TryDeserialize(JsonElement json, [NotNullWhen(true)] out object? value)
    {
        if (TryRead(json, out var typed) && Validate(typed) is null)
        {
            value = typed;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// The type's own checks, such as a range.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns><c>null</c> if the value is valid, or why it isn't.</returns>
    private protected virtual SettingValidationError? ValidateCore(T value) => null;

    /// <summary>
    /// Writes a value as JSON.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The value.</param>
    private protected abstract void Write(Utf8JsonWriter writer, T value);

    /// <summary>
    /// Reads a value from JSON.
    /// </summary>
    /// <param name="json">The JSON value.</param>
    /// <param name="value">The value.</param>
    /// <returns><c>true</c> if the JSON holds a value of the right type.</returns>
    private protected abstract bool TryRead(JsonElement json, [NotNullWhen(true)] out T? value);

    private T Cast(object value) => value is T typed
        ? typed
        : throw new ArgumentException($"Setting {Key} takes a {typeof(T).Name}, not {value?.GetType().Name ?? "null"}.", nameof(value));
}
