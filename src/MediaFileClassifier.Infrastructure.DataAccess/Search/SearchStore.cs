using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using MediaFileClassifier.Infrastructure.DataAccess.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MediaFileClassifier.Infrastructure.DataAccess.Search;

/// <summary>
/// The FTS5 keyword index and the SQL filters of a library (E01-S04-T02, T05). The index rows are written by triggers
/// on <c>media_item</c> (documents and paths) and by <see cref="ItemMetadataRepository"/> (metadata columns), each in
/// the transaction of the change, so a change is searchable as soon as it commits.
/// </summary>
/// <param name="connections">Connection factory of the library.</param>
/// <param name="writer">Writer of the library.</param>
internal sealed class SearchStore(LibraryConnectionFactory connections, LibraryWriter writer) : ISearchStore
{
    /// <summary>
    /// Documents per rebuild write (D072).
    /// </summary>
    public const int RebuildBatchSize = MediaItemRepository.BatchSize;

    /// <inheritdoc />
    public Task<KeywordMatches> FindKeywordMatchesAsync(KeywordSearch search, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentOutOfRangeException.ThrowIfNegative(search.Offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(search.Limit, 1);

        var terms = search.Terms.Where(SearchSql.IsSearchable).Distinct(StringComparer.Ordinal).ToList();
        if (terms.Count == 0)
        {
            return Task.FromResult(new KeywordMatches([], 0));
        }

        var sql = new SearchSql();
        var text = new StringBuilder("WITH ");
        for (var i = 0; i < terms.Count; i++)
        {
            var term = terms[i];
            text.Append(CultureInfo.InvariantCulture, $"""m{i}("doc_id") AS (SELECT "rowid" FROM "search_text" WHERE "search_text" MATCH {sql.Add(SearchSql.QuoteFts(term) + "*")}""");
            if (SearchSql.IsSubstringSearchable(term))
            {
                text.Append(CultureInfo.InvariantCulture, $""" UNION SELECT "rowid" FROM "search_filename" WHERE "search_filename" MATCH {sql.Add(SearchSql.QuoteFts(term))}""");
            }

            text.Append("), ");
        }

        text.Append("""hits("doc_id") AS (""")
            .AppendJoin(" INTERSECT ", Enumerable.Range(0, terms.Count).Select(i => $"""SELECT "doc_id" FROM m{i}"""))
            .Append("), ");

        // BM25 over the documents whose words match every term. Hits that matched a term only as a path substring
        // aren't in it and rank last.
        var all = string.Join(" AND ", terms.Select(term => SearchSql.QuoteFts(term) + "*"));
        text.Append(CultureInfo.InvariantCulture, $"""ranked("doc_id", "rank") AS MATERIALIZED (SELECT "rowid", bm25("search_text", {Weights(search.Weights)}) FROM "search_text" WHERE "search_text" MATCH {sql.Add(all)}) """);

        var where = sql.Where(search.Filter);
        var orderBy = sql.OrderBy(search.Sort, search.Reverse, relevance: "r.\"rank\"");
        text.Append(CultureInfo.InvariantCulture, $"""
            SELECT d."doc_id", d."item_id", r."rank", count(*) OVER ()
            FROM hits h
            JOIN "search_document" d ON d."doc_id" = h."doc_id"
            JOIN "media_item" m ON m."id" = d."item_id"
            {sql.TechnicalJoin}
            LEFT JOIN ranked r ON r."doc_id" = h."doc_id"
            WHERE {where}
            ORDER BY {orderBy}
            LIMIT {sql.Add(search.Limit)} OFFSET {sql.Add(search.Offset)}
            """);

        return connections.ReadAsync(
            async (db, token) =>
            {
                var matches = new List<KeywordMatch>();
                var total = 0;
                await QueryAsync(
                    db,
                    text.ToString(),
                    sql.Parameters,
                    reader =>
                    {
                        var score = reader.IsDBNull(2) ? double.NegativeInfinity : -reader.GetDouble(2);
                        matches.Add(new KeywordMatch(DocumentId(reader, 0), ItemId(reader, 1), score));
                        total = reader.GetInt32(3);
                    },
                    token).ConfigureAwait(false);

                if (matches.Count == 0 && search.Offset > 0)
                {
                    total = await CountKeywordMatchesAsync(search, token).ConfigureAwait(false);
                }

                return new KeywordMatches(matches, total);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int[]> GetDocumentIdsAsync(SearchFilter filter, CancellationToken cancellationToken = default)
    {
        var sql = new SearchSql();
        var where = sql.Where(filter);
        var text = $"""
            SELECT d."doc_id"
            FROM "search_document" d
            JOIN "media_item" m ON m."id" = d."item_id"
            {sql.TechnicalJoin}
            WHERE {where}
            ORDER BY d."doc_id"
            """;

        return connections.ReadAsync(
            async (db, token) =>
            {
                var ids = new List<int>();
                await QueryAsync(db, text, sql.Parameters, reader => ids.Add(DocumentId(reader, 0)), token).ConfigureAwait(false);

                return ids.ToArray();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<DocumentPage> ListAsync(
        SearchFilter filter,
        SortMode sort,
        bool reverse,
        int offset,
        int limit,
        IReadOnlyCollection<int>? onlyDocumentIds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var sql = new SearchSql();
        var where = sql.Where(filter);
        if (onlyDocumentIds is not null)
        {
            where += $"""
                 AND d."doc_id" IN (SELECT "value" FROM json_each({sql.Add(JsonSerializer.Serialize(onlyDocumentIds))}))
                """;
        }

        var orderBy = sql.OrderBy(sort, reverse);
        var text = $"""
            SELECT d."doc_id", d."item_id", count(*) OVER ()
            FROM "media_item" m
            JOIN "search_document" d ON d."item_id" = m."id"
            {sql.TechnicalJoin}
            WHERE {where}
            ORDER BY {orderBy}
            LIMIT {sql.Add(limit)} OFFSET {sql.Add(offset)}
            """;

        return connections.ReadAsync(
            async (db, token) =>
            {
                var documents = new List<SearchDocument>();
                var total = 0;
                await QueryAsync(
                    db,
                    text,
                    sql.Parameters,
                    reader =>
                    {
                        documents.Add(new SearchDocument(DocumentId(reader, 0), ItemId(reader, 1)));
                        total = reader.GetInt32(2);
                    },
                    token).ConfigureAwait(false);

                if (documents.Count == 0 && offset > 0)
                {
                    // Past the last page the window count isn't read; count separately.
                    var countSql = new SearchSql();
                    var countWhere = countSql.Where(filter);
                    if (onlyDocumentIds is not null)
                    {
                        countWhere += $"""
                             AND d."doc_id" IN (SELECT "value" FROM json_each({countSql.Add(JsonSerializer.Serialize(onlyDocumentIds))}))
                            """;
                    }

                    var countText = $"""
                        SELECT count(*) FROM "media_item" m JOIN "search_document" d ON d."item_id" = m."id" {countSql.TechnicalJoin} WHERE {countWhere}
                        """;
                    total = await ExecuteScalarIntAsync(db, countText, countSql.Parameters, token).ConfigureAwait(false);
                }

                return new DocumentPage(documents, total);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<int, SearchFields>> GetMatchedFieldsAsync(
        IReadOnlyCollection<int> documentIds,
        IReadOnlyList<string> terms,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        ArgumentNullException.ThrowIfNull(terms);

        var searchable = terms.Where(SearchSql.IsSearchable).Distinct(StringComparer.Ordinal).ToList();
        if (documentIds.Count == 0 || searchable.Count == 0)
        {
            return Task.FromResult<IReadOnlyDictionary<int, SearchFields>>(new Dictionary<int, SearchFields>());
        }

        var sql = new SearchSql();
        var documents = sql.Add(JsonSerializer.Serialize(documentIds));
        var anyWord = "(" + string.Join(" OR ", searchable.Select(term => SearchSql.QuoteFts(term) + "*")) + ")";
        var parts = new List<string>();
        for (var column = 0; column < SearchSchema.TextColumns.Length; column++)
        {
            parts.Add($"""
                SELECT "rowid", {column} FROM "search_text"
                WHERE "search_text" MATCH {sql.Add("{" + SearchSchema.TextColumns[column] + "} : " + anyWord)}
                AND "rowid" IN (SELECT "value" FROM json_each({documents}))
                """);
        }

        var substrings = searchable.Where(SearchSql.IsSubstringSearchable).ToList();
        if (substrings.Count > 0)
        {
            parts.Add($"""
                SELECT "rowid", {SearchSchema.TextColumns.Length - 1} FROM "search_filename"
                WHERE "search_filename" MATCH {sql.Add(string.Join(" OR ", substrings.Select(SearchSql.QuoteFts)))}
                AND "rowid" IN (SELECT "value" FROM json_each({documents}))
                """);
        }

        var text = string.Join(" UNION ALL ", parts);
        return connections.ReadAsync<IReadOnlyDictionary<int, SearchFields>>(
            async (db, token) =>
            {
                var fields = new Dictionary<int, SearchFields>();
                await QueryAsync(
                    db,
                    text,
                    sql.Parameters,
                    reader =>
                    {
                        var document = DocumentId(reader, 0);
                        fields[document] = fields.GetValueOrDefault(document) | ColumnField(reader.GetInt32(1));
                    },
                    token).ConfigureAwait(false);

                return fields;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> IsRebuildNeededAsync(CancellationToken cancellationToken = default) =>
        connections.ReadAsync(
            async (db, token) =>
            {
                var exists = await ExecuteScalarIntAsync(
                    db,
                    """SELECT count(*) FROM "sqlite_master" WHERE "name" IN ('search_document', 'search_text', 'search_filename', 'search_state')""",
                    [],
                    token).ConfigureAwait(false);
                if (exists < 4)
                {
                    return true;
                }

                return await ExecuteScalarIntAsync(
                    db,
                    """SELECT count(*) FROM "search_state" WHERE "key" = $key""",
                    [new SqliteParameter("$key", SearchSchema.RebuildNeededKey)],
                    token).ConfigureAwait(false) > 0;
            },
            cancellationToken);

    /// <inheritdoc />
    public async Task RebuildAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        // Recreate what's missing, mark the index, and bring the documents in step with the items. From here on the
        // triggers keep documents and paths current while the batches below rewrite every row.
        var total = await writer.ExecuteAsync(
            async (db, token) =>
            {
                foreach (var statement in SearchSchema.EnsureCreatedStatements)
                {
                    await db.Database.ExecuteSqlRawAsync(statement, token).ConfigureAwait(false);
                }

                await db.Database.ExecuteSqlRawAsync(
                    """INSERT OR REPLACE INTO "search_state" ("key", "value") VALUES ({0}, '1')""",
                    [SearchSchema.RebuildNeededKey],
                    token).ConfigureAwait(false);
                await db.Database.ExecuteSqlRawAsync(
                    """DELETE FROM "search_document" WHERE "item_id" NOT IN (SELECT "id" FROM "media_item")""",
                    token).ConfigureAwait(false);
                await db.Database.ExecuteSqlRawAsync(
                    """
                    INSERT INTO "search_document" ("item_id")
                    SELECT "id" FROM "media_item" WHERE "id" NOT IN (SELECT "item_id" FROM "search_document") ORDER BY "id"
                    """,
                    token).ConfigureAwait(false);
                await db.Database.ExecuteSqlRawAsync(
                    """DELETE FROM "search_text" WHERE "rowid" NOT IN (SELECT "doc_id" FROM "search_document")""",
                    token).ConfigureAwait(false);
                await db.Database.ExecuteSqlRawAsync(
                    """DELETE FROM "search_filename" WHERE "rowid" NOT IN (SELECT "doc_id" FROM "search_document")""",
                    token).ConfigureAwait(false);
                return await ExecuteScalarIntAsync(db, """SELECT count(*) FROM "search_document" """, [], token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        progress?.Report(0);
        var done = 0;
        var last = 0L;
        while (true)
        {
            var (count, lastInBatch) = await writer.ExecuteAsync(
                (db, token) => RebuildBatchAsync(db, last, token),
                cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }

            last = lastInBatch;
            done += count;
            progress?.Report(total == 0 ? 1 : Math.Min(1, (double)done / total));
        }

        await writer.ExecuteAsync(
            async (db, token) =>
            {
                await db.Database.ExecuteSqlRawAsync("""INSERT INTO "search_text" ("search_text") VALUES ('optimize')""", token).ConfigureAwait(false);
                await db.Database.ExecuteSqlRawAsync("""INSERT INTO "search_filename" ("search_filename") VALUES ('optimize')""", token).ConfigureAwait(false);
                await db.Database.ExecuteSqlRawAsync(
                    """DELETE FROM "search_state" WHERE "key" = {0}""",
                    [SearchSchema.RebuildNeededKey],
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
        progress?.Report(1);
    }

    /// <summary>
    /// Writes an item's metadata columns, in the caller's transaction.
    /// </summary>
    /// <param name="db">The writer's context.</param>
    /// <param name="metadata">The item's metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the row is written.</returns>
    internal static Task UpdateTextAsync(LibraryDbContext db, ItemMetadata metadata, CancellationToken cancellationToken) =>
        SearchText.From(metadata).UpdateAsync(db, metadata.ItemId.ToString(), cancellationToken);

    private static async Task<(int Count, long Last)> RebuildBatchAsync(LibraryDbContext db, long after, CancellationToken cancellationToken)
    {
        var batch = new List<(long Document, string ItemId, string Path)>();
        await QueryAsync(
            db,
            """
            SELECT d."doc_id", d."item_id", m."path"
            FROM "search_document" d JOIN "media_item" m ON m."id" = d."item_id"
            WHERE d."doc_id" > $after
            ORDER BY d."doc_id"
            LIMIT $limit
            """,
            [new SqliteParameter("$after", after), new SqliteParameter("$limit", RebuildBatchSize)],
            reader => batch.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2))),
            cancellationToken).ConfigureAwait(false);

        if (batch.Count == 0)
        {
            return (0, after);
        }

        var documents = JsonSerializer.Serialize(batch.Select(entry => entry.Document));
        await db.Database.ExecuteSqlRawAsync(
            """DELETE FROM "search_text" WHERE "rowid" IN (SELECT "value" FROM json_each({0}))""",
            [documents],
            cancellationToken).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """DELETE FROM "search_filename" WHERE "rowid" IN (SELECT "value" FROM json_each({0}))""",
            [documents],
            cancellationToken).ConfigureAwait(false);

        var metadata = await ItemMetadataRepository.LoadManyAsync(
            db,
            [.. batch.Select(entry => Id<MediaItem>.Parse(entry.ItemId))],
            cancellationToken).ConfigureAwait(false);
        foreach (var (document, itemId, path) in batch)
        {
            await SearchText.From(metadata[Id<MediaItem>.Parse(itemId)]).InsertAsync(db, document, path, cancellationToken).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync(
                """INSERT INTO "search_filename" ("rowid", "path") VALUES ({0}, {1})""",
                [document, SearchSql.Fold(path)],
                cancellationToken).ConfigureAwait(false);
        }

        return (batch.Count, batch[^1].Document);
    }

    private static string Weights(KeywordFieldWeights weights)
    {
        weights.Validate();
        double[] values = [weights.Title, weights.Description, weights.Tags, weights.OcrText, weights.Transcript, weights.Other, weights.FileName];
        return string.Join(", ", values.Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
    }

    private static SearchFields ColumnField(int column) => column switch
    {
        0 => SearchFields.Title,
        1 => SearchFields.Description,
        2 => SearchFields.Tags,
        3 => SearchFields.OcrText,
        4 => SearchFields.Transcript,
        5 => SearchFields.Other,
        _ => SearchFields.FileName,
    };

    private static int DocumentId(DbDataReader reader, int ordinal) => checked((int)reader.GetInt64(ordinal));

    private static Id<MediaItem> ItemId(DbDataReader reader, int ordinal) => Id<MediaItem>.Parse(reader.GetString(ordinal));

    private static async Task QueryAsync(
        LibraryDbContext db,
        string text,
        IReadOnlyList<SqliteParameter> parameters,
        Action<DbDataReader> onRow,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(db, text, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            onRow(reader);
        }
    }

    private static async Task<int> ExecuteScalarIntAsync(
        LibraryDbContext db,
        string text,
        IReadOnlyList<SqliteParameter> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(db, text, parameters);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "SearchSql puts every value in a parameter; the text holds only code-built SQL and numbers.")]
    private static DbCommand CreateCommand(LibraryDbContext db, string text, IReadOnlyList<SqliteParameter> parameters)
    {
        var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = text;
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        foreach (var parameter in parameters)
        {
            // A parameter belongs to one command; copy it so a SearchSql can serve two queries.
            command.Parameters.Add(new SqliteParameter(parameter.ParameterName, parameter.Value));
        }

        return command;
    }

    private async Task<int> CountKeywordMatchesAsync(KeywordSearch search, CancellationToken cancellationToken)
    {
        var all = await FindKeywordMatchesAsync(search with { Offset = 0, Limit = 1 }, cancellationToken).ConfigureAwait(false);
        return all.TotalCount;
    }
}
