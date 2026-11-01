namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// Rules about the kinds of <see cref="DescriptiveField"/> values.
/// </summary>
public static class DescriptiveFields
{
    /// <summary>
    /// Gets every descriptive field.
    /// </summary>
    public static IReadOnlyList<DescriptiveField> All { get; } = Enum.GetValues<DescriptiveField>();

    /// <summary>
    /// Checks whether a field holds a list of entries rather than one text.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns><c>true</c> for list fields.</returns>
    public static bool IsList(DescriptiveField field) => field >= DescriptiveField.Objects;

    /// <summary>
    /// Checks whether only the user can provide a field.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns><c>true</c> for <see cref="DescriptiveField.Notes"/>.</returns>
    public static bool IsUserOnly(DescriptiveField field) => field == DescriptiveField.Notes;

    /// <summary>
    /// Checks that a value fits a field and that the source may provide it.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The value.</param>
    /// <param name="source">Where the value comes from.</param>
    /// <exception cref="ArgumentException">The value has the wrong kind, or the field is user-only.</exception>
    public static void EnsureValid(DescriptiveField field, FieldValue value, MetadataSource source)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.IsList != IsList(field))
        {
            throw new ArgumentException($"{field} needs a {(IsList(field) ? "list" : "text")} value.", nameof(value));
        }

        if (source != MetadataSource.User && IsUserOnly(field))
        {
            throw new ArgumentException($"{field} can only be written by the user.", nameof(source));
        }
    }
}
