using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaFileClassifier.Infrastructure.DataAccess.Migrations
{
    /// <summary>
    /// Adds one-character prefixes to the keyword index (MFC-988): <c>search_text</c> is recreated with
    /// <c>prefix = '1 2 3'</c> and its rows are copied over, so the index stays complete and needs no rebuild. Without
    /// the one-character prefix index, a query like "b" or "train s" read every token starting with that letter for
    /// each matched-fields lookup (seconds at 100k items). The triggers on <c>media_item</c> name the table and work
    /// with the new one. Hand-written: EF Core doesn't model FTS5 tables. <c>Search/SearchSchema.cs</c> holds the same
    /// DDL for rebuilds.
    /// </summary>
    public partial class SearchPrefixIndex : Migration
    {
        private const string Columns = """
            "title", "description", "tags", "ocr_text", "transcript", "other", "file_name"
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => Recreate(migrationBuilder, "1 2 3");

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => Recreate(migrationBuilder, "2 3");

        private static void Recreate(MigrationBuilder migrationBuilder, string prefixes)
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
                    "title", "description", "tags", "ocr_text", "transcript", "other", "file_name",
                    tokenize = 'unicode61 remove_diacritics 2',
                    prefix = '{prefixes}'
                );
                """);
            migrationBuilder.Sql(
                $"""
                INSERT INTO "search_text" ("rowid", {Columns})
                    SELECT "doc_id", {Columns} FROM "search_text_copy" ORDER BY "doc_id";
                """);
            migrationBuilder.Sql("""DROP TABLE "search_text_copy";""");
            migrationBuilder.Sql("""INSERT INTO "search_text" ("search_text") VALUES ('optimize');""");
        }
    }
}
