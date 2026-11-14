namespace MediaFileClassifier.Infrastructure.DataAccess.Search;

/// <summary>
/// The keyword index's tables and triggers, as the <c>SearchIndex</c> migration creates them, with the prefix index
/// of the <c>SearchPrefixIndex</c> migration (MFC-988) and the sort keys of the <c>SearchSortKeys</c> migration (MFC-762). A rebuild runs these statements first, so it also repairs an
/// index whose tables were dropped. Keep them identical to the migrations; a test compares the two.
/// </summary>
internal static class SearchSchema
{
    /// <summary>
    /// The <c>search_state</c> key that marks the index for a rebuild.
    /// </summary>
    public const string RebuildNeededKey = "rebuild_needed";

    /// <summary>
    /// Writes the sort keys of every document, replacing what is there: the rebuild's repair, and the migration's fill.
    /// </summary>
    public const string FillSortStatement = """
        INSERT OR REPLACE INTO "search_sort" ("doc_id", "name_key", "item_date", "size_bytes", "imported_at")
        SELECT d."doc_id", natural_key(m."path"), coalesce(t."date_taken" - coalesce(t."date_taken_offset", 0), m."file_modified_at"), m."size_bytes", m."imported_at"
        FROM "search_document" d
        JOIN "media_item" m ON m."id" = d."item_id"
        LEFT JOIN "technical_metadata" t ON t."item_id" = m."id"
        """;

    /// <summary>
    /// The keyword fields, in column order of <c>search_text</c>.
    /// </summary>
    public static readonly string[] TextColumns = ["title", "description", "tags", "ocr_text", "transcript", "other", "file_name"];

    /// <summary>
    /// Statements that create whatever is missing.
    /// </summary>
    public static readonly string[] EnsureCreatedStatements =
    [
        """
        CREATE TABLE IF NOT EXISTS "search_document" (
            "doc_id" INTEGER PRIMARY KEY AUTOINCREMENT,
            "item_id" TEXT NOT NULL UNIQUE,
            "excluded" INTEGER NOT NULL DEFAULT 0
        );
        """,
        """
        CREATE VIRTUAL TABLE IF NOT EXISTS "search_text" USING fts5(
            "title", "description", "tags", "ocr_text", "transcript", "other", "file_name",
            tokenize = 'unicode61 remove_diacritics 2',
            prefix = '1 2 3'
        );
        """,
        """
        CREATE VIRTUAL TABLE IF NOT EXISTS "search_filename" USING fts5("path", tokenize = 'trigram remove_diacritics 1');
        """,
        """
        CREATE TABLE IF NOT EXISTS "search_state" (
            "key" TEXT NOT NULL PRIMARY KEY,
            "value" TEXT NOT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS "search_sort" (
            "doc_id" INTEGER PRIMARY KEY,
            "name_key" TEXT,
            "item_date" INTEGER,
            "size_bytes" INTEGER,
            "imported_at" INTEGER
        );
        """,
        """
        CREATE INDEX IF NOT EXISTS "search_sort_name" ON "search_sort" ("name_key");
        """,
        """
        CREATE INDEX IF NOT EXISTS "search_sort_date" ON "search_sort" ("item_date");
        """,
        """
        CREATE INDEX IF NOT EXISTS "search_sort_size" ON "search_sort" ("size_bytes");
        """,
        """
        CREATE INDEX IF NOT EXISTS "search_sort_imported" ON "search_sort" ("imported_at");
        """,
        """
        CREATE TRIGGER IF NOT EXISTS "search_item_insert" AFTER INSERT ON "media_item"
        BEGIN
            INSERT INTO "search_document" ("item_id", "excluded") VALUES (new."id", new."status" = 5);
            INSERT INTO "search_text" ("rowid", "file_name")
                VALUES ((SELECT "doc_id" FROM "search_document" WHERE "item_id" = new."id"), replace(replace(new."path", 'ё', 'е'), 'Ё', 'Е'));
            INSERT INTO "search_filename" ("rowid", "path")
                VALUES ((SELECT "doc_id" FROM "search_document" WHERE "item_id" = new."id"), replace(replace(new."path", 'ё', 'е'), 'Ё', 'Е'));
            INSERT INTO "search_sort" ("doc_id", "name_key", "item_date", "size_bytes", "imported_at")
                VALUES ((SELECT "doc_id" FROM "search_document" WHERE "item_id" = new."id"), natural_key(new."path"), new."file_modified_at", new."size_bytes", new."imported_at");
        END;
        """,
        """
        CREATE TRIGGER IF NOT EXISTS "search_item_path" AFTER UPDATE OF "path" ON "media_item" WHEN new."path" IS NOT old."path"
        BEGIN
            UPDATE "search_text" SET "file_name" = replace(replace(new."path", 'ё', 'е'), 'Ё', 'Е')
                WHERE "rowid" = (SELECT "doc_id" FROM "search_document" WHERE "item_id" = new."id");
            UPDATE "search_filename" SET "path" = replace(replace(new."path", 'ё', 'е'), 'Ё', 'Е')
                WHERE "rowid" = (SELECT "doc_id" FROM "search_document" WHERE "item_id" = new."id");
            UPDATE "search_sort" SET "name_key" = natural_key(new."path")
                WHERE "doc_id" = (SELECT "doc_id" FROM "search_document" WHERE "item_id" = new."id");
        END;
        """,
        """
        CREATE TRIGGER IF NOT EXISTS "search_item_status" AFTER UPDATE OF "status" ON "media_item" WHEN (new."status" = 5) IS NOT (old."status" = 5)
        BEGIN
            UPDATE "search_document" SET "excluded" = (new."status" = 5) WHERE "item_id" = new."id";
        END;
        """,
        """
        CREATE TRIGGER IF NOT EXISTS "search_item_delete" AFTER DELETE ON "media_item"
        BEGIN
            DELETE FROM "search_text" WHERE "rowid" = (SELECT "doc_id" FROM "search_document" WHERE "item_id" = old."id");
            DELETE FROM "search_filename" WHERE "rowid" = (SELECT "doc_id" FROM "search_document" WHERE "item_id" = old."id");
            DELETE FROM "search_sort" WHERE "doc_id" = (SELECT "doc_id" FROM "search_document" WHERE "item_id" = old."id");
            DELETE FROM "search_document" WHERE "item_id" = old."id";
        END;
        """,
        """
        CREATE TRIGGER IF NOT EXISTS "search_item_sort" AFTER UPDATE OF "file_modified_at", "size_bytes", "imported_at" ON "media_item"
        BEGIN
            UPDATE "search_sort" SET
                "item_date" = (SELECT coalesce(t."date_taken" - coalesce(t."date_taken_offset", 0), m."file_modified_at") FROM "media_item" m LEFT JOIN "technical_metadata" t ON t."item_id" = m."id" WHERE m."id" = new."id"),
                "size_bytes" = new."size_bytes",
                "imported_at" = new."imported_at"
                WHERE "doc_id" = (SELECT "doc_id" FROM "search_document" WHERE "item_id" = new."id");
        END;
        """,
        """
        CREATE TRIGGER IF NOT EXISTS "search_technical_insert" AFTER INSERT ON "technical_metadata"
        BEGIN
            UPDATE "search_sort" SET
                "item_date" = (SELECT coalesce(t."date_taken" - coalesce(t."date_taken_offset", 0), m."file_modified_at") FROM "media_item" m LEFT JOIN "technical_metadata" t ON t."item_id" = m."id" WHERE m."id" = new."item_id")
                WHERE "doc_id" = (SELECT "doc_id" FROM "search_document" WHERE "item_id" = new."item_id");
        END;
        """,
        """
        CREATE TRIGGER IF NOT EXISTS "search_technical_date" AFTER UPDATE OF "date_taken", "date_taken_offset" ON "technical_metadata"
        BEGIN
            UPDATE "search_sort" SET
                "item_date" = (SELECT coalesce(t."date_taken" - coalesce(t."date_taken_offset", 0), m."file_modified_at") FROM "media_item" m LEFT JOIN "technical_metadata" t ON t."item_id" = m."id" WHERE m."id" = new."item_id")
                WHERE "doc_id" = (SELECT "doc_id" FROM "search_document" WHERE "item_id" = new."item_id");
        END;
        """,
        """
        CREATE TRIGGER IF NOT EXISTS "search_technical_delete" AFTER DELETE ON "technical_metadata"
        BEGIN
            UPDATE "search_sort" SET
                "item_date" = (SELECT coalesce(t."date_taken" - coalesce(t."date_taken_offset", 0), m."file_modified_at") FROM "media_item" m LEFT JOIN "technical_metadata" t ON t."item_id" = m."id" WHERE m."id" = old."item_id")
                WHERE "doc_id" = (SELECT "doc_id" FROM "search_document" WHERE "item_id" = old."item_id");
        END;
        """,
    ];
}
