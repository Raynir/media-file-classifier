namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// Normalized tag text. Whitespace is trimmed and collapsed, and names compare ignoring case,
/// so "Beach" from AI and "beach" from the user are the same tag.
/// </summary>
public sealed record TagName
{
    private TagName(string value) => Value = value;

    /// <summary>
    /// Gets the display text, in the case it was first written.
    /// </summary>
    public string Value { get; }

    public static TagName From(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        return new TagName(string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
    }

    public bool Equals(TagName? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    /// <inheritdoc/>
    public override string ToString() => Value;
}
