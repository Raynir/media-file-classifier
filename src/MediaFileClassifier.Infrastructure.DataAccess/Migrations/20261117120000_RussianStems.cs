using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaFileClassifier.Infrastructure.DataAccess.Migrations
{
    /// <summary>
    /// Adds the <c>stems</c> column to the keyword index (MFC-328, D349): the Snowball stems of the Russian words of each
    /// item's metadata, so a Russian query word also matches other forms of it. FTS5 tables can't gain columns, so
    /// <c>search_text</c> is recreated and its rows copied, with the stems computed by the <c>ru_stem</c> function every
    /// library connection registers; no rebuild is needed. The triggers on <c>media_item</c> name the table and work
    /// with the new one. Hand-written: EF Core doesn't model FTS5 tables. <c>Search/SearchSchema.cs</c> holds the same DDL
    /// for rebuilds.
    /// </summary>
    public partial class RussianStems : Migration
    {
        private const string Columns = """
            "title", "description", "tags", "ocr_text", "transcript", "other", "file_name"
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            Recreate(
                migrationBuilder,
                ", \"stems\"",
                ", ru_stem(concat_ws(char(10), \"title\", \"description\", \"tags\", \"ocr_text\", \"transcript\", \"other\"))");

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => Recreate(migrationBuilder, string.Empty, string.Empty);

        private static void Recreate(MigrationBuilder migrationBuilder, string stemsColumn, string stemsValue)
        {
            // Through a plain table: renaming the FTS table would rewrite the triggers to name the old one.
            migrationBuilder.Sql(
                $"""
                CREATE TEMP TABLE "search_text_copy" AS SELECT "rowid" AS "doc_id", {Columns} FROM "search_text";
                """);
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
