namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// Value of a descriptive field: one text, or a list of entries. Values compare by content.
/// </summary>
/// <remarks>
/// <para>
/// An empty text or an empty list is a valid user value: it means the user cleared the field, which hides the AI value.
/// </para>
/// <para>
/// A text value may carry its form in the other language (<see cref="OtherForm"/>, D035, E06-S04-T09), such as the
/// Russian title a describer wrote. It travels with the value, so accepting an AI value keeps it (D047), and a value
/// the user types has none. It is indexed for keyword search but doesn't take part in comparisons: the same text with
/// another form is the same value.
/// </para>
/// </remarks>
public sealed record FieldValue
{
    private FieldValue(bool isList, IReadOnlyList<string> items)
    {
        IsList = isList;
        Items = items;
    }

    public bool IsList { get; }

    /// <summary>
    /// Gets the entries of a list value, or the single text of a text value.
    /// </summary>
    public IReadOnlyList<string> Items { get; }

    /// <summary>
    /// Gets the text of a text value, or the entries of a list value joined with commas.
    /// </summary>
    public string Text => IsList ? string.Join(", ", Items) : Items[0];

    /// <summary>
    /// Gets the text in the other language (Russian for English text), or <c>null</c>. Only text values have one.
    /// </summary>
    public string? OtherForm { get; private init; }

    public static FieldValue FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new FieldValue(false, [text.Trim()]);
    }

    /// <summary>
    /// Creates a list value. Entries are trimmed, and blank and repeated entries (ignoring case) are dropped.
    /// </summary>
    /// <param name="items">The entries.</param>
    /// <returns>The value.</returns>
    public static FieldValue FromList(IEnumerable<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var entries = items
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new FieldValue(true, entries);
    }

    /// <summary>
    /// Gives a text value its form in the other language.
    /// </summary>
    /// <param name="form">The form, or <c>null</c>. A blank form, or one equal to the text ignoring case, gives none.</param>
    /// <returns>The value with that form.</returns>
    /// <exception cref="InvalidOperationException">The value is a list.</exception>
    public FieldValue WithOtherForm(string? form)
    {
        if (IsList)
        {
            throw new InvalidOperationException("Only a text value has a form in the other language.");
        }

        var trimmed = form?.Trim();
        return this with
        {
            OtherForm = string.IsNullOrEmpty(trimmed) || string.Equals(trimmed, Items[0], StringComparison.OrdinalIgnoreCase) ? null : trimmed,
        };
    }

    public bool Equals(FieldValue? other) =>
        other is not null && IsList == other.IsList && Items.SequenceEqual(other.Items, StringComparer.Ordinal);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(IsList);
        foreach (var item in Items)
        {
            hash.Add(item, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public override string ToString() => Text;
}
