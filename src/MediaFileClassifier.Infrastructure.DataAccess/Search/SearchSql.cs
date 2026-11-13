using System.Globalization;
using System.Text;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using Microsoft.Data.Sqlite;

namespace MediaFileClassifier.Infrastructure.DataAccess.Search;

/// <summary>
/// Translates filters, orders and keyword terms into SQL (E01-S04-T05). Queries start from <c>search_document</c> as
/// <c>d</c>, and join <c>media_item</c> as <c>m</c> and <c>technical_metadata</c> as <c>t</c> only when
/// <see cref="NeedsMediaItem"/> and <see cref="NeedsTechnicalMetadata"/> say so. Every value is a parameter; only numbers the code formats itself go into the text.
/// </summary>
internal sealed class SearchSql : IFilterSql
{
    // The item date: date taken, converted to UTC with its offset when known, else the file modification time. Dates
    // taken are wall-clock milliseconds and offsets milliseconds (D071).
    private const string ItemDate = """coalesce(t."date_taken" - coalesce(t."date_taken_offset", 0), m."file_modified_at")""";

    // The file name: the path after its last '/'. rtrim strips the trailing non-slash characters, which leaves the folder.
    private const string FileName = """substr(m."path", length(rtrim(m."path", replace(m."path", '/', ''))) + 1)""";

    private readonly List<SqliteParameter> parameters = [];

    /// <summary>
    /// Gets the parameters added so far.
    /// </summary>
    public IReadOnlyList<SqliteParameter> Parameters => parameters;

    /// <summary>
    /// Gets whether the SQL built so far reads <c>m</c>, so the query must join <c>media_item</c>.
    /// </summary>
    public bool NeedsMediaItem { get; private set; }

    /// <summary>
    /// Gets whether the SQL built so far reads <c>t</c>, so the query must join <c>technical_metadata</c>.
    /// </summary>
    public bool NeedsTechnicalMetadata { get; private set; }

    /// <summary>
    /// Gets the joins of <c>media_item</c> and <c>technical_metadata</c> to <c>d</c>, each only if it's needed.
    /// </summary>
    public string Joins =>
        (NeedsMediaItem ? """JOIN "media_item" m ON m."id" = d."item_id" """ : string.Empty)
        + (NeedsTechnicalMetadata ? """LEFT JOIN "technical_metadata" t ON t."item_id" = d."item_id" """ : string.Empty);

    /// <inheritdoc />
    public string ItemId => "d.\"item_id\"";

    /// <inheritdoc />
    string IFilterSql.ItemDate
    {
        get
        {
            NeedsTechnicalMetadata = true;
            NeedsMediaItem = true;
            return ItemDate;
        }
    }

    /// <summary>
    /// Quotes a term as an FTS5 string, so its characters are never read as query syntax.
    /// </summary>
    /// <param name="term">The term.</param>
    /// <returns>The FTS5 string.</returns>
    public static string QuoteFts(string term) => "\"" + Fold(term).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>
    /// Folds what the tokenizers don't: <c>unicode61</c> removes Latin diacritics only, so Russian <c>ё</c> becomes
    /// <c>е</c> here, as people often type it (D029). Indexed text and query terms are both folded; the triggers do the
    /// same to paths in SQL.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The folded text.</returns>
    public static string Fold(string text) => text.Replace('ё', 'е').Replace('Ё', 'Е');

    /// <summary>
    /// Checks whether a term has a letter or digit, so the tokenizer produces a token from it. Others would make an
    /// empty FTS5 phrase.
    /// </summary>
    /// <param name="term">The term.</param>
    /// <returns><c>true</c> if the term can match.</returns>
    public static bool IsSearchable(string term) => term.Any(char.IsLetterOrDigit);

    /// <summary>
    /// Checks whether a term is long enough for the trigram index: three characters or more.
    /// </summary>
    /// <param name="term">The term.</param>
    /// <returns><c>true</c> if the term can match a path substring.</returns>
    public static bool IsSubstringSearchable(string term) => term.EnumerateRunes().Count() >= 3;

    /// <summary>
    /// Adds a parameter.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>Its name.</returns>
    public string Add(object? value)
    {
        var name = "$p" + parameters.Count.ToString(CultureInfo.InvariantCulture);
        parameters.Add(new SqliteParameter(name, value ?? DBNull.Value));
        return name;
    }

    /// <inheritdoc />
    public string MediaItem(string column)
    {
        NeedsMediaItem = true;
        return "m." + Column(column);
    }

    /// <inheritdoc />
    public string TechnicalMetadata(string column)
    {
        NeedsTechnicalMetadata = true;
        return "t." + Column(column);
    }

    /// <inheritdoc />
    public string Parameter(object? value) => Add(value);

    /// <inheritdoc />
    public string Like(string expression, string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        return $"{expression} LIKE {Add(pattern)} ESCAPE '{SqlLike.EscapeCharacter}'";
    }

    /// <inheritdoc />
    public string EscapeLike(string text) => SqlLike.Escape(text);

    /// <summary>
    /// Builds the condition of a filter. Excluded items never pass (D048, D024); <c>search_document</c> mirrors the flag,
    /// so that check needs no join.
    /// </summary>
    /// <param name="filter">The filter.</param>
    /// <returns>The condition, for a <c>WHERE</c> clause.</returns>
    public string Where(SearchFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var conditions = new List<string> { """d."excluded" = 0""" };

        if (filter.MediaTypes is { } types)
        {
            conditions.Add($"""m."media_type" IN ({Numbers(types.Select(type => (int)type))})""");
        }

        if (filter.Formats is { } formats)
        {
            var patterns = formats
                .Select(format => format.Trim().TrimStart('.'))
                .Where(format => format.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(format => $"""m."path" LIKE {Add("%." + SqlLike.Escape(format))} ESCAPE '{SqlLike.EscapeCharacter}'""")
                .ToList();

            // LIKE ignores case for ASCII, which covers file extensions.
            conditions.Add(patterns.Count == 0 ? "0" : "(" + string.Join(" OR ", patterns) + ")");
        }

        if (filter.DateFrom is { } from)
        {
            NeedsTechnicalMetadata = true;
            conditions.Add($"{ItemDate} >= {Add(from.ToUnixTimeMilliseconds())}");
        }

        if (filter.DateTo is { } to)
        {
            NeedsTechnicalMetadata = true;
            conditions.Add($"{ItemDate} < {Add(to.ToUnixTimeMilliseconds())}");
        }

        if (filter.MinPixels is { } minPixels)
        {
            NeedsTechnicalMetadata = true;
            conditions.Add($"""t."width" * t."height" >= {Add(minPixels)}""");
        }

        if (filter.MaxPixels is { } maxPixels)
        {
            NeedsTechnicalMetadata = true;
            conditions.Add($"""t."width" * t."height" <= {Add(maxPixels)}""");
        }

        if (filter.IsFavorite is { } favorite)
        {
            conditions.Add($"""m."is_favorite" = {(favorite ? 1 : 0)}""");
        }

        if (filter.Statuses is { } statuses)
        {
            conditions.Add($"""m."status" IN ({Numbers(statuses.Select(status => (int)status))})""");
        }

        if (filter.ReviewStatus is { } review)
        {
            conditions.Add($"""m."review_status" = {(int)review}""");
        }

        if (filter.CollectionId is { } collection)
        {
            conditions.Add(
                $"""EXISTS (SELECT 1 FROM "collection_membership" c WHERE c."item_id" = m."id" AND c."collection_id" = {Add(collection.ToString())})""");
        }

        foreach (var tag in filter.UserTags ?? [])
        {
            // tag_key is the tag in upper case (D071), so this ignores case in every alphabet.
            conditions.Add(
                $"""EXISTS (SELECT 1 FROM "tag_assignment" a WHERE a."item_id" = m."id" AND a."source" = {(int)MetadataSource.User} AND a."tag_key" = {Add(tag.Value.ToUpperInvariant())})""");
        }

        if (filter.FolderId is { } folder)
        {
            conditions.Add($"""m."folder_id" = {Add(folder.ToString())}""");
        }

        if (SqlLike.SubfolderPattern(filter.Subfolder) is { } pattern)
        {
            conditions.Add($"""m."path" LIKE {Add(pattern)} ESCAPE '{SqlLike.EscapeCharacter}'""");
        }

        if (filter.ImportedSince is { } since)
        {
            conditions.Add($"""m."imported_at" >= {Add(since.ToUnixTimeMilliseconds())}""");
        }

        // Registered filters (E10-S01) build their own conditions through IFilterSql, which joins what they read.
        foreach (var condition in filter.Conditions ?? [])
        {
            if (condition.ToSql(this) is { Length: > 0 } sql)
            {
                conditions.Add("(" + sql + ")");
            }
        }

        NeedsMediaItem |= conditions.Any(ReadsMediaItem);
        return string.Join(" AND ", conditions);
    }

    /// <summary>
    /// Builds an <c>ORDER BY</c> list. Every order ends with the document ID, so pages never overlap.
    /// </summary>
    /// <param name="sort">The order.</param>
    /// <param name="reverse">Whether to reverse it.</param>
    /// <param name="relevance">The relevance expression, best first when ascending, or <c>null</c> to order
    /// <see cref="SortMode.Relevance"/> by newest import.</param>
    /// <returns>The list, without the keywords.</returns>
    public string OrderBy(SortMode sort, bool reverse, string? relevance = null)
    {
        (string Expression, bool Ascending)[] keys = sort switch
        {
            SortMode.Name => [($"{FileName} COLLATE NOCASE", true), ("""m."path" """, true)],
            SortMode.Date => [(ItemDate, false)],
            SortMode.Size => [("""m."size_bytes" """, false)],
            SortMode.ImportDate => [("""m."imported_at" """, false), ("""m."id" """, false)],
            _ when relevance is not null => [($"{relevance} IS NULL", true), (relevance, true)],
            _ => [("""m."id" """, false)],
        };

        if (sort == SortMode.Date)
        {
            NeedsTechnicalMetadata = true;
        }

        NeedsMediaItem |= keys.Any(key => ReadsMediaItem(key.Expression));

        var builder = new StringBuilder();
        foreach (var (expression, ascending) in keys.Append(("""d."doc_id" """, keys[^1].Ascending)))
        {
            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            builder.Append(expression.Trim()).Append(ascending != reverse ? " ASC" : " DESC");
        }

        return builder.ToString();
    }

    // Every column of media_item is written as m."column", and no other alias ends in m.
    private static bool ReadsMediaItem(string sql) => sql.Contains("m.\"", StringComparison.Ordinal);

    // Column names go into the SQL text, so only plain identifiers pass.
    private static string Column(string column) =>
        column is { Length: > 0 } && column.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_')
            ? "\"" + column + "\""
            : throw new ArgumentException($"'{column}' isn't a column name.", nameof(column));

    private static string Numbers(IEnumerable<int> values)
    {
        var list = values.Distinct().ToList();
        return list.Count == 0 ? "NULL" : string.Join(", ", list.Select(value => value.ToString(CultureInfo.InvariantCulture)));
    }
}
