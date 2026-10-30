namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// Value of a descriptive field: one text, or a list of entries. Values compare by content.
/// </summary>
/// <remarks>
/// An empty text or an empty list is a valid user value: it means the user cleared the field, which hides the AI value.
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
