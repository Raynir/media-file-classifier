using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;

namespace MediaFileClassifier.Infrastructure.DataAccess.Search.Filters;

/// <summary>
/// The file format filter (E10-S03): the formats of the format registry (E03-S01), so the list grows as formats are
/// added. Values are format IDs (e.g. <c>jpeg</c>); an item matches by its file extension, any of the format's
/// extensions. Formats the registry no longer knows are ignored, and a value with none known matches nothing.
/// </summary>
/// <param name="formats">The format registry.</param>
internal sealed class FileFormatFilter(IMediaFormatRegistry formats) : FilterDefinition<MultiSelectValue>, IFilterOptions
{
    /// <summary>
    /// The filter's ID.
    /// </summary>
    public const string FilterId = "file-format";

    /// <inheritdoc />
    public override string Id => FilterId;

    /// <inheritdoc />
    public override string DisplayNameKey => "Filter_FileFormat";

    /// <inheritdoc />
    public override int Order => 200;

    /// <inheritdoc />
    public override FilterValueKind Kind => FilterValueKind.MultiSelect;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FilterOption>> GetOptionsAsync(string? prefix, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<FilterOption>>(
            [.. formats.Handlers
                .Where(handler => string.IsNullOrEmpty(prefix) || handler.FormatId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(handler => new FilterOption(handler.FormatId, DisplayName: handler.FormatId.ToUpperInvariant()))]);

    /// <inheritdoc />
    protected override string? ToSql(MultiSelectValue value, IFilterSql sql)
    {
        var path = sql.MediaItem("path");

        // LIKE ignores case for ASCII, which covers file extensions.
        var patterns = value.Values
            .Select(id => formats.FindByFormatId(id))
            .OfType<IMediaFormatHandler>()
            .SelectMany(handler => handler.Extensions)
            .Select(extension => extension.Trim().TrimStart('.'))
            .Where(extension => extension.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(extension => sql.Like(path, "%." + sql.EscapeLike(extension)))
            .ToList();

        return patterns.Count == 0 ? "0" : string.Join(" OR ", patterns);
    }
}
