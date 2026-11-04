using System.Globalization;
using System.Text;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using Microsoft.Data.Sqlite;

namespace MediaFileClassifier.Infrastructure.DataAccess.Search;

/// <summary>
/// Translates filters, orders and keyword terms into SQL (E01-S04-T05). Queries alias <c>media_item</c> as <c>m</c>,
/// <c>search_document</c> as <c>d</c> and, when <see cref="NeedsTechnicalMetadata"/> says so, <c>technical_metadata</c>
/// as <c>t</c>. Every value is a parameter; only numbers the code formats itself go into the text.
/// </summary>
internal sealed class SearchSql
{
    private const string LikeEscape = "\\";

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
    /// Gets whether the SQL built so far reads <c>t</c>, so the query must join <c>technical_metadata</c>.
    /// </summary>
    public bool NeedsTechnicalMetadata { get; private set; }

    /// <summary>
    /// Gets the join of <c>technical_metadata</c>, or nothing if it isn't needed.
    /// </summary>
    public string TechnicalJoin => NeedsTechnicalMetadata ? """LEFT JOIN "technical_metadata" t ON t."item_id" = m."id" """ : string.Empty;

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

    /// <summary>
    /// Builds the condition of a filter. Excluded items never pass (D048, D024).
    /// </summary>
    /// <param name="filter">The filter.</param>
    /// <returns>The condition, for a <c>WHERE</c> clause.</returns>
    public string Where(SearchFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var conditions = new List<string> { $"""m."status" <> {(int)MediaItemStatus.Excluded}""" };

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
                .Select(format => $"""m."path" LIKE {Add("%." + EscapeLike(format))} ESCAPE '{LikeEscape}'""")
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

        if (filter.Subfolder?.Replace('\\', '/').Trim('/') is { Length: > 0 } subfolder)
        {
            conditions.Add($"""m."path" LIKE {Add(EscapeLike(subfolder) + "/%")} ESCAPE '{LikeEscape}'""");
        }

        if (filter.ImportedSince is { } since)
        {
            conditions.Add($"""m."imported_at" >= {Add(since.ToUnixTimeMilliseconds())}""");
        }

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

    private static string Numbers(IEnumerable<int> values)
    {
        var list = values.Distinct().ToList();
        return list.Count == 0 ? "NULL" : string.Join(", ", list.Select(value => value.ToString(CultureInfo.InvariantCulture)));
    }

    private static string EscapeLike(string value) =>
        value.Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal);
}
