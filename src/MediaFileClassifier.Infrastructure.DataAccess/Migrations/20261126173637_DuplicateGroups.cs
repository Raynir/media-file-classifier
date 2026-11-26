using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaFileClassifier.Infrastructure.DataAccess.Migrations
{
    /// <summary>
    /// Library schema 20: duplicate groups (MFC-825, E12-S01-T01). Adds <c>duplicate_group</c> (type, state, the shared
    /// content hash of an exact group) and <c>duplicate_group_member</c>. Hand-written part: triggers on
    /// <c>media_item</c> record in <c>duplicate_dirty_hash</c>, in the same transaction, each content hash whose items
    /// changed (added, hash changed, Missing or Excluded or back, deleted) when another item has it or an exact group
    /// does, and the duplicate maintenance reconciles the groups of those hashes (E12-S01-T02). The hashes that existing items already share are marked
    /// too, so an upgraded library gets its groups on first open.
    /// </summary>
    public partial class DuplicateGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "duplicate_group",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    type = table.Column<int>(type: "INTEGER", nullable: false),
                    state = table.Column<int>(type: "INTEGER", nullable: false),
                    content_hash = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    state_changed_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_duplicate_group", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "duplicate_group_member",
                columns: table => new
                {
                    group_id = table.Column<string>(type: "TEXT", nullable: false),
                    item_id = table.Column<string>(type: "TEXT", nullable: false),
                    joined_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_duplicate_group_member", x => new { x.group_id, x.item_id });
                    table.ForeignKey(
                        name: "FK_duplicate_group_member_duplicate_group_group_id",
                        column: x => x.group_id,
                        principalTable: "duplicate_group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_duplicate_group_member_media_item_item_id",
                        column: x => x.item_id,
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_duplicate_group_state",
                table: "duplicate_group",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "IX_duplicate_group_type_content_hash",
                table: "duplicate_group",
                columns: new[] { "type", "content_hash" },
                unique: true,
                filter: "\"content_hash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_duplicate_group_member_item_id",
                table: "duplicate_group_member",
                column: "item_id");

            migrationBuilder.Sql("""CREATE TABLE "duplicate_dirty_hash" ("content_hash" TEXT NOT NULL PRIMARY KEY) WITHOUT ROWID;""");
            // A hash is queued only when it can change a group: another item has it, or a group has it.
            migrationBuilder.Sql("""
                CREATE TRIGGER "duplicate_item_insert" AFTER INSERT ON "media_item"
                WHEN new."hashes_content_hash" IS NOT NULL
                    AND EXISTS (SELECT 1 FROM "media_item" WHERE "hashes_content_hash" = new."hashes_content_hash" AND "id" <> new."id")
                BEGIN
                    INSERT OR IGNORE INTO "duplicate_dirty_hash" ("content_hash") VALUES (new."hashes_content_hash");
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "duplicate_item_update" AFTER UPDATE OF "hashes_content_hash", "status" ON "media_item"
                WHEN new."hashes_content_hash" IS NOT old."hashes_content_hash" OR (new."status" IN (4, 5)) IS NOT (old."status" IN (4, 5))
                BEGIN
                    INSERT OR IGNORE INTO "duplicate_dirty_hash" ("content_hash")
                    SELECT old."hashes_content_hash"
                    WHERE EXISTS (SELECT 1 FROM "duplicate_group" WHERE "type" = 0 AND "content_hash" = old."hashes_content_hash");
                    INSERT OR IGNORE INTO "duplicate_dirty_hash" ("content_hash")
                    SELECT new."hashes_content_hash"
                    WHERE EXISTS (SELECT 1 FROM "media_item" WHERE "hashes_content_hash" = new."hashes_content_hash" AND "id" <> new."id")
                        OR EXISTS (SELECT 1 FROM "duplicate_group" WHERE "type" = 0 AND "content_hash" = new."hashes_content_hash");
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "duplicate_item_delete" AFTER DELETE ON "media_item"
                WHEN EXISTS (SELECT 1 FROM "duplicate_group" WHERE "type" = 0 AND "content_hash" = old."hashes_content_hash")
                BEGIN
                    INSERT OR IGNORE INTO "duplicate_dirty_hash" ("content_hash") VALUES (old."hashes_content_hash");
                END;
                """);
            migrationBuilder.Sql("""
                INSERT OR IGNORE INTO "duplicate_dirty_hash" ("content_hash")
                SELECT "hashes_content_hash" FROM "media_item" WHERE "hashes_content_hash" IS NOT NULL
                GROUP BY "hashes_content_hash" HAVING count(*) > 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "duplicate_item_insert";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "duplicate_item_update";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "duplicate_item_delete";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "duplicate_dirty_hash";""");

            migrationBuilder.DropTable(
                name: "duplicate_group_member");

            migrationBuilder.DropTable(
                name: "duplicate_group");
        }
    }
}
