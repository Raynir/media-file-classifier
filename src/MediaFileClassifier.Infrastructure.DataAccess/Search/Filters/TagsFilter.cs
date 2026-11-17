using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Infrastructure.DataAccess.Database.Conversions;

namespace MediaFileClassifier.Infrastructure.DataAccess.Search.Filters;

/// <summary>
/// The tag filter (E10-S03): items with all (AND) or any (OR) of the included tags, and none of the excluded ones. It
/// matches effective tags, as the item details show them (<see cref="EffectiveTagSql"/>): a rejected AI tag, or a tag of
/// a rejected or inactive run, never matches. Tags compare ignoring case and extra white space.
/// </summary>
/// <param name="session">The open library, whose tags the filter offers.</param>
internal sealed class TagsFilter(ILibrarySession session) : FilterDefinition<IncludeExcludeValue>, IFilterOptions
{
    /// <summary>
    /// The filter's ID.
    /// </summary>
    public const string FilterId = "tags";

    /// <summary>
    /// The most tags autocompletion offers.
    /// </summary>
    public const int OptionLimit = 20;

    /// <inheritdoc />
    public override string Id => FilterId;

    /// <inheritdoc />
    public override string DisplayNameKey => "Filter_Tags";

    /// <inheritdoc />
    public override int Order => 700;

    /// <inheritdoc />
    public override FilterValueKind Kind => FilterValueKind.IncludeExclude;

    /// <summary>
    /// Lists the effective tags that start with the typed text, ignoring case, most used first; nothing without an open
    /// library.
    /// </summary>
    /// <param name="prefix">The typed text, or <see langword="null"/> for the most used tags.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>Up to <see cref="OptionLimit"/> tags, each its own value and label.</returns>
    public async ValueTask<IReadOnlyList<FilterOption>> GetOptionsAsync(string? prefix, CancellationToken cancellationToken)
    {
        if (session.Current is not { } library)
        {
            return [];
        }

        var tags = await library.Metadata.GetEffectiveTagsAsync(prefix, OptionLimit, cancellationToken).ConfigureAwait(false);
        return [.. tags.Select(tag => new FilterOption(tag.Tag.Value, DisplayName: tag.Tag.Value))];
    }

    /// <inheritdoc />
    protected override string? ToSql(IncludeExcludeValue value, IFilterSql sql)
    {
        var conditions = new List<string>();
        var include = Keys(value.Include);
        if (include.Count > 0)
        {
            // All of them: one probe per key, as a tag matches by its own key or by its other-language form's.
            conditions.AddRange(value.MatchAll
                ? include.Select(key => $"EXISTS {Assignments([key], sql)}")
                : [$"EXISTS {Assignments(include, sql)}"]);
        }

        var exclude = Keys(value.Exclude);
        if (exclude.Count > 0)
        {
            conditions.Add($"NOT EXISTS {Assignments(exclude, sql)}");
        }

        return string.Join(" AND ", conditions);
    }

    // tag_key and other_form_key are the tag and its other-language form in upper case (D071), so this ignores case in
    // every alphabet, and a tag in either language finds the tag.
    private static List<string> Keys(IEnumerable<string> tags) =>
        [.. tags.Select(tag => TagKeyGenerator.KeyOf(TagName.From(tag))).Distinct(StringComparer.Ordinal)];

    private static string Assignments(IEnumerable<string> keys, IFilterSql sql) => $"(SELECT 1 {From(keys, sql)})";

    private static string From(IEnumerable<string> keys, IFilterSql sql)
    {
        var list = string.Join(", ", keys.Select(sql.Parameter).ToList());
        return $"""FROM "tag_assignment" a WHERE a."item_id" = {sql.ItemId} AND (a."tag_key" IN ({list}) OR a."other_form_key" IN ({list})) AND {EffectiveTagSql.Condition("a")}""";
    }
}
