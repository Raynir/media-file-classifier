using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaFileClassifier.Infrastructure.DataAccess.Migrations
{
    /// <summary>
    /// Library schema 14: Russian keyword search.
    /// <list type="bullet">
    /// <item>Adds the <c>stems</c> column to the keyword index (MFC-328, D349): the Snowball stems of the Russian words of
    /// each item's metadata, so a Russian query word also matches other forms of it.</item>
    /// <item>Gives each tag assignment its other-language form, <c>other_form</c>, marked by <c>is_other_form_derived</c>
    /// when it comes from the tag dictionary (MFC-325, D035, D047, D348). Existing tags get their dictionary form through
    /// the <c>tag_other_form</c> function, and the forms of each item's effective tags are added to its <c>tags</c>
    /// column.</item>
    /// </list>
    /// FTS5 tables can't gain columns, so <c>search_text</c> is recreated and its rows copied, with the stems computed by
    /// the <c>ru_stem</c> function; both functions are registered on every library connection, and no rebuild is needed.
    /// The triggers on <c>media_item</c> name the table and work with the new one. Hand-written: EF Core doesn't model
    /// FTS5 tables. <c>Search/SearchSchema.cs</c> holds the same DDL for rebuilds.
    /// </summary>
    public partial class RussianStems : Migration
    {
        private const string Columns = """
            "title", "description", "tags", "ocr_text", "transcript", "other", "file_name"
            """;

        /// <summary>
        /// Adds the other forms of an item's effective tags to its copied row. "Effective" is the condition of
        /// <c>Search/EffectiveTagSql.cs</c> as of schema 14, written out so the migration doesn't change with the code:
        /// sources Technical = 0, AI = 1, User = 2, run status Completed = 1. The forms are folded like the index text
        /// ("ё" as "е").
        /// </summary>
        private const string AddTagForms = """
            UPDATE "search_text_copy" SET "tags" = concat_ws(char(10), "search_text_copy"."tags", f."forms")
            FROM (
                SELECT d."doc_id", replace(replace(replace(group_concat(DISTINCT t."other_form"), ',', char(10)), 'ё', 'е'), 'Ё', 'Е') AS "forms"
                FROM "search_document" d
                JOIN "tag_assignment" t ON t."item_id" = d."item_id"
                WHERE t."other_form" IS NOT NULL
                    AND (t."source" = 2
                        OR (NOT EXISTS (SELECT 1 FROM "tag_rejection" x WHERE x."item_id" = t."item_id" AND x."tag_key" = t."tag_key")
                            AND (t."source" = 0
                                OR (t."source" = 1 AND EXISTS (
                                    SELECT 1 FROM "analysis_run" r
                                    WHERE r."id" = t."run_id" AND r."is_active" = 1 AND r."status" = 1 AND r."rejected_at" IS NULL)))))
                GROUP BY d."doc_id"
            ) AS f
            WHERE f."doc_id" = "search_text_copy"."doc_id";
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_other_form_derived",
                table: "tag_assignment",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "other_form",
                table: "tag_assignment",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "tag_assignment" SET "other_form" = tag_other_form("tag"), "is_other_form_derived" = 1
                WHERE tag_other_form("tag") IS NOT NULL;
                """);

            Recreate(
                migrationBuilder,
                ", \"stems\"",
                ", ru_stem(concat_ws(char(10), \"title\", \"description\", \"tags\", \"ocr_text\", \"transcript\", \"other\"))",
                AddTagForms);
        }

        /// <inheritdoc />
        /// <remarks>The other forms stay in the <c>tags</c> column until the next rebuild or metadata write.</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            Recreate(migrationBuilder, string.Empty, string.Empty, null);
            migrationBuilder.DropColumn(name: "is_other_form_derived", table: "tag_assignment");
            migrationBuilder.DropColumn(name: "other_form", table: "tag_assignment");
        }

        private static void Recreate(MigrationBuilder migrationBuilder, string stemsColumn, string stemsValue, string copyUpdate)
        {
            // Through a plain table: renaming the FTS table would rewrite the triggers to name the old one.
            migrationBuilder.Sql(
                $"""
                CREATE TEMP TABLE "search_text_copy" AS SELECT "rowid" AS "doc_id", {Columns} FROM "search_text";
                """);
            if (copyUpdate is not null)
            {
                migrationBuilder.Sql(copyUpdate);
            }

            migrationBuilder.Sql("""DROP TABLE "search_text";""");
            migrationBuilder.Sql(
                $"""
                CREATE VIRTUAL TABLE "search_text" USING fts5(
                    "title", "description", "tags", "ocr_text", "transcript", "other", "file_name"{stemsColumn},
                    tokenize = 'unicode61 remove_diacritics 2',
                    prefix = '1 2 3'
                );
                """);
            migrationBuilder.Sql(
                $"""
                INSERT INTO "search_text" ("rowid", {Columns}{stemsColumn})
                    SELECT "doc_id", {Columns}{stemsValue} FROM "search_text_copy" ORDER BY "doc_id";
                """);
            migrationBuilder.Sql("""DROP TABLE "search_text_copy";""");
            migrationBuilder.Sql("""INSERT INTO "search_text" ("search_text") VALUES ('optimize');""");
        }
    }
}
