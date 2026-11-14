using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaFileClassifier.Infrastructure.DataAccess.Migrations
{
    /// <summary>
    /// Adds the sort keys (MFC-762, library schema 12): <c>search_sort</c> holds each document's natural name key, item
    /// date, size and import time, each indexed, so a page in any order reads an index instead of sorting every item.
    /// The triggers on <c>media_item</c> and <c>technical_metadata</c> keep it current; the name key comes from the
    /// <c>natural_key</c> function every library connection registers. Hand-written: the table isn't in the EF model.
    /// <c>Search/SearchSchema.cs</c> holds the same DDL for rebuilds.
    /// </summary>
    public partial class SearchSortKeys : Migration
    {
        private const string ItemDate = """
            (SELECT coalesce(t."date_taken" - coalesce(t."date_taken_offset", 0), m."file_modified_at") FROM "media_item" m LEFT JOIN "technical_metadata" t ON t."item_id" = m."id" WHERE m."id" =
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE "search_sort" (
                    "doc_id" INTEGER PRIMARY KEY,
                    "name_key" TEXT,
                    "item_date" INTEGER,
                    "size_bytes" INTEGER,
                    "imported_at" INTEGER
                );
                """);
            migrationBuilder.Sql(
                """
                INSERT INTO "search_sort" ("doc_id", "name_key", "item_date", "size_bytes", "imported_at")
                SELECT d."doc_id", natural_key(m."path"), coalesce(t."date_taken" - coalesce(t."date_taken_offset", 0), m."file_modified_at"), m."size_bytes", m."imported_at"
                FROM "search_document" d
                JOIN "media_item" m ON m."id" = d."item_id"
                LEFT JOIN "technical_metadata" t ON t."item_id" = m."id";
                """);
            migrationBuilder.Sql("""CREATE INDEX "search_sort_name" ON "search_sort" ("name_key");""");
            migrationBuilder.Sql("""CREATE INDEX "search_sort_date" ON "search_sort" ("item_date");""");
            migrationBuilder.Sql("""CREATE INDEX "search_sort_size" ON "search_sort" ("size_bytes");""");
            migrationBuilder.Sql("""CREATE INDEX "search_sort_imported" ON "search_sort" ("imported_at");""");

            DropItemTriggers(migrationBuilder);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER "search_item_insert" AFTER INSERT ON "media_item"
                BEGIN
                    INSERT INTO "search_document" ("item_id", "excluded") VALUES (new."id", new."status" = 5);
                    INSERT INTO "search_text" ("rowid", "file_name")
                        VALUES ((SELECT "doc_id" FROM "search_document" WHERE "item_id" = new."id"), replace(replace(new."path", 'ё', 'е'), 'Ё', 'Е'));
                    INSERT INTO "search_filename" ("rowid", "path")
                        VALUES ((SELECT "doc_id" FROM "search_document" WHERE "item_id" = new."id"), replace(replace(new."path", 'ё', 'е'), 'Ё', 'Е'));
                    INSERT INTO "search_sort" ("doc_id", "name_key", "item_date", "size_bytes", "imported_at")
                        VALUES ((SELECT "doc_id" FROM "search_document" WHERE "item_id" = new."id"), natural_key(new."path"), new."file_modified_at", new."size_bytes", new."imported_at");
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
                    UPDATE "search_sort" SET "name_key" = natural_key(new."path")
                        WHERE "doc_id" = (SELECT "doc_id" FROM "search_document" WHERE "item_id" = new."id");
                END;
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER "search_item_delete" AFTER DELETE ON "media_item"
                BEGIN
                    DELETE FROM "search_text" WHERE "rowid" = (SELECT "doc_id" FROM "search_document" WHERE "item_id" = old."id");
                    DELETE FROM "search_filename" WHERE "rowid" = (SELECT "doc_id" FROM "search_document" WHERE "item_id" = old."id");
                    DELETE FROM "search_sort" WHERE "doc_id" = (SELECT "doc_id" FROM "search_document" WHERE "item_id" = old."id");
                    DELETE FROM "search_document" WHERE "item_id" = old."id";
                END;
                """);
            migrationBuilder.Sql(
                $"""
                CREATE TRIGGER "search_item_sort" AFTER UPDATE OF "file_modified_at", "size_bytes", "imported_at" ON "media_item"
                BEGIN
                    UPDATE "search_sort" SET
                        "item_date" = {ItemDate} new."id"),
                        "size_bytes" = new."size_bytes",
                        "imported_at" = new."imported_at"
                        WHERE "doc_id" = (SELECT "doc_id" FROM "search_document" WHERE "item_id" = new."id");
                END;
                """);
            migrationBuilder.Sql(
                $"""
                CREATE TRIGGER "search_technical_insert" AFTER INSERT ON "technical_metadata"
                BEGIN
                    UPDATE "search_sort" SET
                        "item_date" = {ItemDate} new."item_id")
                        WHERE "doc_id" = (SELECT "doc_id" FROM "search_document" WHERE "item_id" = new."item_id");
                END;
                """);
            migrationBuilder.Sql(
                $"""
                CREATE TRIGGER "search_technical_date" AFTER UPDATE OF "date_taken", "date_taken_offset" ON "technical_metadata"
                BEGIN
                    UPDATE "search_sort" SET
                        "item_date" = {ItemDate} new."item_id")
                        WHERE "doc_id" = (SELECT "doc_id" FROM "search_document" WHERE "item_id" = new."item_id");
                END;
                """);
            migrationBuilder.Sql(
                $"""
                CREATE TRIGGER "search_technical_delete" AFTER DELETE ON "technical_metadata"
                BEGIN
                    UPDATE "search_sort" SET
                        "item_date" = {ItemDate} old."item_id")
                        WHERE "doc_id" = (SELECT "doc_id" FROM "search_document" WHERE "item_id" = old."item_id");
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TRIGGER "search_technical_delete";""");
            migrationBuilder.Sql("""DROP TRIGGER "search_technical_date";""");
            migrationBuilder.Sql("""DROP TRIGGER "search_technical_insert";""");
            migrationBuilder.Sql("""DROP TRIGGER "search_item_sort";""");
            DropItemTriggers(migrationBuilder);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER "search_item_insert" AFTER INSERT ON "media_item"
                BEGIN
                    INSERT INTO "search_document" ("item_id", "excluded") VALUES (new."id", new."status" = 5);
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
            migrationBuilder.Sql("""DROP TABLE "search_sort";""");
        }

        private static void DropItemTriggers(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TRIGGER "search_item_insert";""");
            migrationBuilder.Sql("""DROP TRIGGER "search_item_path";""");
            migrationBuilder.Sql("""DROP TRIGGER "search_item_delete";""");
        }
    }
}
