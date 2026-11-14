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

        var all = string.Join(" AND ", terms.Select(term => SearchSql.QuoteFts(term) + "*"));
        var fast = search is { NeedsTotal: false, Sort: SortMode.Relevance, Reverse: false, Offset: 0 } ? TopMatchesQuery(search, all) : ((string Text, IReadOnlyList<SqliteParameter> Parameters)?)null;
        var (text, parameters) = AllMatchesQuery(search, terms, all);

        return connections.ReadAsync(
            async (db, token) =>
            {
                // A full page of the best BM25 matches is the page the full query would give: hits found only as a path
                // substring rank after every BM25 match.
                if (fast is { } top)
                {
                    var best = await ReadMatchesAsync(db, top.Text, top.Parameters, withTotal: false, token).ConfigureAwait(false);
                    if (best.Matches.Count == search.Limit)
                    {
                        return best with { TotalCount = best.Matches.Count };
                    }
                }

                var page = await ReadMatchesAsync(db, text, parameters, withTotal: true, token).ConfigureAwait(false);
                if (page.Matches.Count == 0 && search.Offset > 0)
                {
                    page = page with { TotalCount = await CountKeywordMatchesAsync(search, token).ConfigureAwait(false) };
                }

                return page;
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
            {sql.Joins}
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
        var where = OnlyDocuments(sql, sql.Where(filter), onlyDocumentIds);
        var orderBy = sql.OrderBy(sort, reverse);
        var text = $"""
            SELECT d."doc_id", d."item_id"
            FROM "search_document" d
            {sql.Joins}
            WHERE {where}
            ORDER BY {orderBy}
            LIMIT {sql.Add(limit)} OFFSET {sql.Add(offset)}
            """;

        // Counted on its own: a count(*) OVER () window would read and sort every row, so no sort key index could give
        // the page (MFC-762).
        var countSql = new SearchSql();
        var countWhere = OnlyDocuments(countSql, countSql.Where(filter), onlyDocumentIds);
        var countText = $"""
            SELECT count(*) FROM "search_document" d {countSql.Joins} WHERE {countWhere}
            """;

        return connections.ReadAsync(
            async (db, token) =>
            {
                // One read transaction, so the page and the total come from the same snapshot while items are added.
                await using var snapshot = await db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
                var documents = new List<SearchDocument>();
                await QueryAsync(
                    db,
                    text,
                    sql.Parameters,
                    reader => documents.Add(new SearchDocument(DocumentId(reader, 0), ItemId(reader, 1))),
                    token).ConfigureAwait(false);

                // A short first page is the whole list.
                var total = offset == 0 && documents.Count < limit
                    ? documents.Count
                    : await ExecuteScalarIntAsync(db, countText, countSql.Parameters, token).ConfigureAwait(false);
                return new DocumentPage(documents, total);
            },
            cancellationToken);
    }

    private static string OnlyDocuments(SearchSql sql, string where, IReadOnlyCollection<int>? documentIds) =>
        documentIds is null
            ? where
            : where + $"""
                 AND d."doc_id" IN (SELECT "value" FROM json_each({sql.Add(JsonSerializer.Serialize(documentIds))}))
                """;

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

        // One lookup per document: a column-filtered MATCH would scan the term's whole doclist for every column
        // (≈95 ms each at 100k items), while a MATCH pinned to one rowid reads just that row. highlight() marks the
        // matched phrases, so a marker in a column means the column matched.
        var columns = string.Join(
            ", ",
            Enumerable.Range(0, SearchSchema.TextColumns.Length).Select(column =>
                $"""coalesce(instr(highlight("search_text", {column}, char(1), ''), char(1)) > 0, 0)"""));
        var text = $"""SELECT {columns} FROM "search_text" WHERE "search_text" MATCH $match AND "rowid" = $doc""";
        var anyWord = string.Join(" OR ", searchable.Select(term => SearchSql.QuoteFts(term) + "*"));

        // Path substrings: the trigram index's text, read by rowid and searched here, ignoring case and diacritics
        // the way the trigram tokenizer does.
        var substrings = searchable.Where(SearchSql.IsSubstringSearchable).Select(SearchSql.Fold).ToList();
        const string PathsText = """SELECT "rowid", "path" FROM "search_filename" WHERE "rowid" IN (SELECT "value" FROM json_each($docs))""";

        return connections.ReadAsync<IReadOnlyDictionary<int, SearchFields>>(
            async (db, token) =>
            {
                var fields = new Dictionary<int, SearchFields>();
                await using (var command = CreateCommand(db, text, [new SqliteParameter("$match", anyWord), new SqliteParameter("$doc", 0L)]))
                {
                    var doc = command.Parameters["$doc"];
                    foreach (var document in documentIds)
                    {
                        doc.Value = (long)document;
                        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                        if (!await reader.ReadAsync(token).ConfigureAwait(false))
                        {
                            continue;
                        }

                        var matched = SearchFields.None;
                        for (var column = 0; column < SearchSchema.TextColumns.Length; column++)
                        {
                            if (reader.GetInt64(column) != 0)
                            {
                                matched |= ColumnField(column);
                            }
                        }

                        if (matched != SearchFields.None)
                        {
                            fields[document] = matched;
                        }
                    }
                }

                if (substrings.Count > 0)
                {
                    var compare = CultureInfo.InvariantCulture.CompareInfo;
                    await QueryAsync(
                        db,
                        PathsText,
                        [new SqliteParameter("$docs", JsonSerializer.Serialize(documentIds))],
                        reader =>
                        {
                            var path = reader.GetString(1);
                            if (substrings.Any(term => compare.IndexOf(path, term, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0))
                            {
                                var document = DocumentId(reader, 0);
                                fields[document] = fields.GetValueOrDefault(document) | SearchFields.FileName;
                            }
                        },
                        token).ConfigureAwait(false);
                }

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
                    """SELECT count(*) FROM "sqlite_master" WHERE "name" IN ('search_document', 'search_text', 'search_filename', 'search_state', 'search_sort')""",
                    [],
                    token).ConfigureAwait(false);
                if (exists < 5)
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
                    INSERT INTO "search_document" ("item_id", "excluded")
                    SELECT "id", "status" = {0} FROM "media_item" WHERE "id" NOT IN (SELECT "item_id" FROM "search_document") ORDER BY "id"
                    """,
                    [(int)MediaItemStatus.Excluded],
                    token).ConfigureAwait(false);
                await db.Database.ExecuteSqlRawAsync(
                    """
                    UPDATE "search_document" SET "excluded" = m."status" = {0}
                    FROM "media_item" m WHERE m."id" = "search_document"."item_id" AND "excluded" IS NOT (m."status" = {0})
                    """,
                    [(int)MediaItemStatus.Excluded],
                    token).ConfigureAwait(false);
                await db.Database.ExecuteSqlRawAsync(
                    """DELETE FROM "search_text" WHERE "rowid" NOT IN (SELECT "doc_id" FROM "search_document")""",
                    token).ConfigureAwait(false);
                await db.Database.ExecuteSqlRawAsync(
                    """DELETE FROM "search_filename" WHERE "rowid" NOT IN (SELECT "doc_id" FROM "search_document")""",
                    token).ConfigureAwait(false);
                await db.Database.ExecuteSqlRawAsync(
                    """DELETE FROM "search_sort" WHERE "doc_id" NOT IN (SELECT "doc_id" FROM "search_document")""",
                    token).ConfigureAwait(false);
                await db.Database.ExecuteSqlRawAsync(SearchSchema.FillSortStatement, token).ConfigureAwait(false);
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

    // Every match: hits of each term (word prefix, or path substring for 3+ characters), intersected, with BM25 over the
    // documents whose words match every term, counted and ordered as a whole.
    private static (string Text, IReadOnlyList<SqliteParameter> Parameters) AllMatchesQuery(KeywordSearch search, List<string> terms, string all)
    {
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
        text.Append(CultureInfo.InvariantCulture, $"""ranked("doc_id", "rank") AS MATERIALIZED (SELECT "rowid", bm25("search_text", {Weights(search.Weights)}) FROM "search_text" WHERE "search_text" MATCH {sql.Add(all)}) """);

        var where = sql.Where(search.Filter);
        var orderBy = sql.OrderBy(search.Sort, search.Reverse, relevance: "r.\"rank\"");
        text.Append(CultureInfo.InvariantCulture, $"""
            SELECT d."doc_id", d."item_id", r."rank", count(*) OVER ()
            FROM hits h
            JOIN "search_document" d ON d."doc_id" = h."doc_id"
            {sql.Joins}
            LEFT JOIN ranked r ON r."doc_id" = h."doc_id"
            WHERE {where}
            ORDER BY {orderBy}
            LIMIT {sql.Add(search.Limit)} OFFSET {sql.Add(search.Offset)}
            """);
        return (text.ToString(), sql.Parameters);
    }

    // The best BM25 matches of the first page only (MFC-989): no intersection, no count, and SQLite keeps just the top
    // rows while it sorts. Same order as the full query's: rank, then document ID.
    private static (string Text, IReadOnlyList<SqliteParameter> Parameters) TopMatchesQuery(KeywordSearch search, string all)
    {
        var sql = new SearchSql();
        var match = sql.Add(all);
        var where = sql.Where(search.Filter);
        var text = $"""
            SELECT d."doc_id", d."item_id", f."rank"
            FROM (SELECT "rowid" AS "doc_id", bm25("search_text", {Weights(search.Weights)}) AS "rank" FROM "search_text" WHERE "search_text" MATCH {match}) f
            JOIN "search_document" d ON d."doc_id" = f."doc_id"
            {sql.Joins}
            WHERE {where}
            ORDER BY f."rank" ASC, d."doc_id" ASC
            LIMIT {sql.Add(search.Limit)}
            """;
        return (text, sql.Parameters);
    }

    private static async Task<KeywordMatches> ReadMatchesAsync(
        LibraryDbContext db,
        string text,
        IReadOnlyList<SqliteParameter> parameters,
        bool withTotal,
        CancellationToken cancellationToken)
    {
        var matches = new List<KeywordMatch>();
        var total = 0;
        await QueryAsync(
            db,
            text,
            parameters,
            reader =>
            {
                var score = reader.IsDBNull(2) ? double.NegativeInfinity : -reader.GetDouble(2);
                matches.Add(new KeywordMatch(DocumentId(reader, 0), ItemId(reader, 1), score));
                total = withTotal ? reader.GetInt32(3) : matches.Count;
            },
            cancellationToken).ConfigureAwait(false);
        return new KeywordMatches(matches, total);
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
        var all = await FindKeywordMatchesAsync(search with { Offset = 0, Limit = 1, NeedsTotal = true }, cancellationToken).ConfigureAwait(false);
        return all.TotalCount;
    }
}
