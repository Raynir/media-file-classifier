using System.Text.Json.Serialization;

namespace MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;

/// <summary>
/// The value of one filter (E10-S01). Values are immutable and compare by content, so a filter state survives a round
/// trip through JSON unchanged. Lists are kept sorted and without duplicates, so their order never matters.
/// </summary>
public abstract record FilterValue
{
    /// <summary>
    /// Gets a value indicating whether the value filters nothing, e.g. nothing selected. An empty value is not stored in
    /// a filter state.
    /// </summary>
    [JsonIgnore]
    public abstract bool IsEmpty { get; }

    /// <summary>
    /// Sorts and de-duplicates a list of values, dropping blank ones.
    /// </summary>
    /// <param name="values">The values, or <see langword="null"/>.</param>
    /// <returns>The normalised list.</returns>
    protected static IReadOnlyList<string> Normalise(IEnumerable<string>? values) =>
        [.. (values ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>
    /// Hashes a list of values by content.
    /// </summary>
    /// <param name="values">The values.</param>
    /// <returns>The hash.</returns>
    protected static int Hash(IReadOnlyList<string> values)
    {
        var hash = default(HashCode);
        foreach (var value in values)
        {
            hash.Add(value, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// The values picked in a <see cref="FilterValueKind.MultiSelect"/> filter. An item matches if it has any of them
/// (E10 combination rules).
/// </summary>
public sealed record MultiSelectValue : FilterValue
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MultiSelectValue"/> class.
    /// </summary>
    /// <param name="values">The values picked, in any order.</param>
    public MultiSelectValue(IReadOnlyList<string>? values) => Values = Normalise(values);

    /// <summary>
    /// Gets the values picked, sorted.
    /// </summary>
    public IReadOnlyList<string> Values { get; }

    /// <inheritdoc />
    [JsonIgnore]
    public override bool IsEmpty => Values.Count == 0;

    /// <inheritdoc />
    public bool Equals(MultiSelectValue? other) => other is not null && Values.SequenceEqual(other.Values, StringComparer.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => Hash(Values);
}

/// <summary>
/// A numeric range for a <see cref="FilterValueKind.Range"/> filter, both ends inclusive; a <see langword="null"/>
/// end is open.
/// </summary>
/// <param name="Min">The smallest value, or <see langword="null"/>.</param>
/// <param name="Max">The largest value, or <see langword="null"/>.</param>
public sealed record RangeValue(double? Min, double? Max) : FilterValue
{
    /// <inheritdoc />
    [JsonIgnore]
    public override bool IsEmpty => Min is null && Max is null;
}

/// <summary>
/// A date range for a <see cref="FilterValueKind.DateRange"/> filter: a preset the filter defines (e.g.
/// <c>last-7-days</c>), resolved when the search runs so a saved search stays relative, or a custom range.
/// </summary>
/// <param name="Preset">The preset's ID, or <see langword="null"/> for a custom range.</param>
/// <param name="From">The start of a custom range, inclusive, or <see langword="null"/>.</param>
/// <param name="To">The end of a custom range, exclusive, or <see langword="null"/>.</param>
public sealed record DateRangeValue(string? Preset, DateTimeOffset? From, DateTimeOffset? To) : FilterValue
{
    /// <inheritdoc />
    [JsonIgnore]
    public override bool IsEmpty => string.IsNullOrWhiteSpace(Preset) && From is null && To is null;
}

/// <summary>
/// The value of a <see cref="FilterValueKind.Resolution"/> filter: a preset the filter defines (e.g. <c>hd</c>), or a
/// minimum displayed width and height in pixels. A preset wins over the minimums.
/// </summary>
/// <param name="Preset">The preset's ID, or <see langword="null"/> for custom minimums.</param>
/// <param name="MinWidth">The smallest displayed width, or <see langword="null"/>.</param>
/// <param name="MinHeight">The smallest displayed height, or <see langword="null"/>.</param>
public sealed record ResolutionValue(string? Preset, int? MinWidth, int? MinHeight) : FilterValue
{
    /// <inheritdoc />
    [JsonIgnore]
    public override bool IsEmpty => string.IsNullOrWhiteSpace(Preset) && MinWidth is null && MinHeight is null;
}

/// <summary>
/// The value of a <see cref="FilterValueKind.Boolean"/> filter. Both <see langword="true"/> and
/// <see langword="false"/> filter; a filter that's off isn't in the filter state at all.
/// </summary>
/// <param name="Value">The value to keep.</param>
public sealed record BooleanValue(bool Value) : FilterValue
{
    /// <inheritdoc />
    [JsonIgnore]
    public override bool IsEmpty => false;
}

/// <summary>
/// The value of a <see cref="FilterValueKind.IncludeExclude"/> filter, e.g. tags: an item must have all
/// (<see cref="MatchAll"/>) or any of <see cref="Include"/>, and none of <see cref="Exclude"/>.
/// </summary>
public sealed record IncludeExcludeValue : FilterValue
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IncludeExcludeValue"/> class.
    /// </summary>
    /// <param name="include">The values an item must have.</param>
    /// <param name="exclude">The values an item must not have.</param>
    /// <param name="matchAll">Whether an item needs all included values (AND) or any (OR).</param>
    public IncludeExcludeValue(IReadOnlyList<string>? include, IReadOnlyList<string>? exclude, bool matchAll)
    {
        Include = Normalise(include);
        Exclude = Normalise(exclude);
        MatchAll = matchAll;
    }

    /// <summary>
    /// Gets the values an item must have, sorted.
    /// </summary>
    public IReadOnlyList<string> Include { get; }

    /// <summary>
    /// Gets the values an item must not have, sorted.
    /// </summary>
    public IReadOnlyList<string> Exclude { get; }

    /// <summary>
    /// Gets a value indicating whether an item needs all of <see cref="Include"/> (AND) or any of them (OR).
    /// </summary>
    public bool MatchAll { get; }

    /// <inheritdoc />
    [JsonIgnore]
    public override bool IsEmpty => Include.Count == 0 && Exclude.Count == 0;

    /// <inheritdoc />
    public bool Equals(IncludeExcludeValue? other) =>
        other is not null
        && MatchAll == other.MatchAll
        && Include.SequenceEqual(other.Include, StringComparer.Ordinal)
        && Exclude.SequenceEqual(other.Exclude, StringComparer.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Hash(Include), Hash(Exclude), MatchAll);
}

/// <summary>
/// The value of a <see cref="FilterValueKind.Folder"/> filter: indexed folders or subfolders, each written as the
/// folder's ID, or the ID, <c>/</c> and the subfolder's path inside it (e.g. <c>{id}/2024/Trips</c>). An item matches if
/// it is in any of them, directly or, with <see cref="IncludeSubfolders"/>, in a subfolder.
/// </summary>
public sealed record FolderValue : FilterValue
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FolderValue"/> class.
    /// </summary>
    /// <param name="folders">The folders and subfolders, in any order.</param>
    /// <param name="includeSubfolders">Whether items in their subfolders match too.</param>
    public FolderValue(IReadOnlyList<string>? folders, bool includeSubfolders)
    {
        Folders = Normalise(folders);
        IncludeSubfolders = includeSubfolders;
    }

    /// <summary>
    /// Gets the folders and subfolders, sorted.
    /// </summary>
    public IReadOnlyList<string> Folders { get; }

    /// <summary>
    /// Gets a value indicating whether items in the subfolders of <see cref="Folders"/> match too.
    /// </summary>
    public bool IncludeSubfolders { get; }

    /// <inheritdoc />
    [JsonIgnore]
    public override bool IsEmpty => Folders.Count == 0;

    /// <inheritdoc />
    public bool Equals(FolderValue? other) =>
        other is not null && IncludeSubfolders == other.IncludeSubfolders && Folders.SequenceEqual(other.Folders, StringComparer.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Hash(Folders), IncludeSubfolders);
}
