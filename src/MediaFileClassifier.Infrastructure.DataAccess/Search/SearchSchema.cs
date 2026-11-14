namespace MediaFileClassifier.Infrastructure.DataAccess.Search;

/// <summary>
/// The keyword index's tables and triggers, as the <c>SearchIndex</c> migration creates them, with the prefix index
/// of the <c>SearchPrefixIndex</c> migration (MFC-988). A rebuild runs these statements first, so it also repairs an
/// index whose tables were dropped. Keep them identical to the migrations; a test compares the two.
/// </summary>
internal static class SearchSchema
{
    /// <summary>
    /// The <c>search_state</c> key that marks the index for a rebuild.
    /// </summary>
    public const string RebuildNeededKey = "rebuild_needed";

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
        CREATE TRIGGER IF NOT EXISTS "search_item_insert" AFTER INSERT ON "media_item"
        BEGIN
            INSERT INTO "search_document" ("item_id", "excluded") VALUES (new."id", new."status" = 5);
            INSERT INTO "search_text" ("rowid", "file_name")
                VALUES ((SELECT "doc_id" FROM "search_document" WHERE "item_id" = new."id"), replace(replace(new."path", 'ё', 'е'), 'Ё', 'Е'));
            INSERT INTO "search_filename" ("rowid", "path")
                VALUES ((SELECT "doc_id" FROM "search_document" WHERE "item_id" = new."id"), replace(replace(new."path", 'ё', 'е'), 'Ё', 'Е'));
        END;
        """,
        """
        CREATE TRIGGER IF NOT EXISTS "search_item_path" AFTER UPDATE OF "path" ON "media_item" WHEN new."path" IS NOT old."path"
        BEGIN
            UPDATE "search_text" SET "file_name" = replace(replace(new."path", 'ё', 'е'), 'Ё', 'Е')
                WHERE "rowid" = (SELECT "doc_id" FROM "search_document" WHERE "item_id" = new."id");
            UPDATE "search_filename" SET "path" = replace(replace(new."path", 'ё', 'е'), 'Ё', 'Е')
                WHERE "rowid" = (SELECT "doc_id" FROM "search_document" WHERE "item_id" = new."id");
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
            DELETE FROM "search_document" WHERE "item_id" = old."id";
        END;
        """,
    ];
}
