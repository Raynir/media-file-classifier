using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaFileClassifier.Infrastructure.DataAccess.Migrations
{
    /// <summary>
    /// Adds the keyword search index (E01-S04-T02): <c>search_document</c> maps items to stable integer document IDs,
    /// <c>search_text</c> is the FTS5 index over the effective metadata values, and <c>search_filename</c> is a trigram
    /// index for substring matches on the path. Triggers keep the documents and paths in step with <c>media_item</c>;
    /// the metadata columns are written by the metadata repository in the same transaction as the metadata.
    /// Hand-written: EF Core doesn't model FTS5 tables. <c>Search/SearchSchema.cs</c> holds the same DDL for rebuilds.
    /// </summary>
    public partial class SearchIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AUTOINCREMENT: a deleted item's document ID is never reused, so the in-memory vector index (D067) can't
            // give a stale vector to a new item.
            migrationBuilder.Sql(
                """
                CREATE TABLE "search_document" (
                    "doc_id" INTEGER PRIMARY KEY AUTOINCREMENT,
                    "item_id" TEXT NOT NULL UNIQUE
                );
                """);
            migrationBuilder.Sql(
                """
                CREATE VIRTUAL TABLE "search_text" USING fts5(
                    "title", "description", "tags", "ocr_text", "transcript", "other", "file_name",
                    tokenize = 'unicode61 remove_diacritics 2',
                    prefix = '2 3'
                );
                """);
            migrationBuilder.Sql(
                """
                CREATE VIRTUAL TABLE "search_filename" USING fts5("path", tokenize = 'trigram remove_diacritics 1');
                """);
            migrationBuilder.Sql(
                """
                CREATE TABLE "search_state" (
                    "key" TEXT NOT NULL PRIMARY KEY,
                    "value" TEXT NOT NULL
                );
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER "search_item_insert" AFTER INSERT ON "media_item"
                BEGIN
                    INSERT INTO "search_document" ("item_id") VALUES (new."id");
                    INSERT INTO "search_text" ("rowid", "file_name")
                        VALUES ((SELECT "doc_id" FROM "search_document" WHERE "item_id" = new."id"), replace(replace(new."path", 'ё', 'е'), 'Ё', 'Е'));
                    INSERT INTO "search_filename" ("rowid", "path")
                        VALUES ((SELECT "doc_id" FROM "search_document" WHERE "item_id" = new."id"), replace(replace(new."path", 'ё', 'е'), 'Ё', 'Е'));
                END;
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER "search_item_path" AFTER UPDATE OF "path" ON "media_item" WHEN new."path" IS NOT old."path"
                BEGIN
                    UPDATE "search_text" SET "file_name" = replace(replace(new."path", 'ё', 'е'), 'Ё', 'Е')
                        WHERE "rowid" = (SELECT "doc_id" FROM "search_document" WHERE "item_id" = new."id");
                    UPDATE "search_filename" SET "path" = replace(replace(new."path", 'ё', 'е'), 'Ё', 'Е')
                        WHERE "rowid" = (SELECT "doc_id" FROM "search_document" WHERE "item_id" = new."id");
                END;
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER "search_item_delete" AFTER DELETE ON "media_item"
                BEGIN
                    DELETE FROM "search_text" WHERE "rowid" = (SELECT "doc_id" FROM "search_document" WHERE "item_id" = old."id");
                    DELETE FROM "search_filename" WHERE "rowid" = (SELECT "doc_id" FROM "search_document" WHERE "item_id" = old."id");
                    DELETE FROM "search_document" WHERE "item_id" = old."id";
                END;
                """);

            // Existing items get their documents and paths here. Their metadata columns need the effective-value rules,
            // which live in C#, so the app rebuilds the index once after opening the library.
            migrationBuilder.Sql("""INSERT INTO "search_document" ("item_id") SELECT "id" FROM "media_item" ORDER BY "id";""");
            migrationBuilder.Sql(
                """
                INSERT INTO "search_text" ("rowid", "file_name")
                    SELECT d."doc_id", replace(replace(m."path", 'ё', 'е'), 'Ё', 'Е') FROM "search_document" d JOIN "media_item" m ON m."id" = d."item_id";
                """);
            migrationBuilder.Sql(
                """
                INSERT INTO "search_filename" ("rowid", "path")
                    SELECT d."doc_id", replace(replace(m."path", 'ё', 'е'), 'Ё', 'Е') FROM "search_document" d JOIN "media_item" m ON m."id" = d."item_id";
                """);
            migrationBuilder.Sql(
                """
                INSERT INTO "search_state" ("key", "value")
                    SELECT 'rebuild_needed', '1' WHERE EXISTS (SELECT 1 FROM "media_item");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TRIGGER "search_item_delete";""");
            migrationBuilder.Sql("""DROP TRIGGER "search_item_path";""");
            migrationBuilder.Sql("""DROP TRIGGER "search_item_insert";""");
            migrationBuilder.Sql("""DROP TABLE "search_state";""");
            migrationBuilder.Sql("""DROP TABLE "search_filename";""");
            migrationBuilder.Sql("""DROP TABLE "search_text";""");
            migrationBuilder.Sql("""DROP TABLE "search_document";""");
        }
    }
}
