using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaFileClassifier.Infrastructure.DataAccess.Migrations
{
    /// <summary>
    /// Library schema 21: the duplicate resolution log (E12-S04, MFC-144). Adds <c>duplicate_resolution</c> (one entry per
    /// resolved or ignored group: keeper, options, time, undo time and what the metadata merge changed, as JSON) and
    /// <c>duplicate_resolution_member</c> (each member's action and the path its file had). The group and items are plain
    /// columns, since the log outlives them.
    /// </summary>
    public partial class DuplicateResolutionLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "duplicate_resolution",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    group_id = table.Column<string>(type: "TEXT", nullable: false),
                    group_type = table.Column<int>(type: "INTEGER", nullable: false),
                    kind = table.Column<int>(type: "INTEGER", nullable: false),
                    keeper_id = table.Column<string>(type: "TEXT", nullable: true),
                    merged_metadata = table.Column<bool>(type: "INTEGER", nullable: false),
                    removed_from_library = table.Column<bool>(type: "INTEGER", nullable: false),
                    trash_requested = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    undone_at = table.Column<long>(type: "INTEGER", nullable: true),
                    merge = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_duplicate_resolution", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "duplicate_resolution_member",
                columns: table => new
                {
                    resolution_id = table.Column<string>(type: "TEXT", nullable: false),
                    item_id = table.Column<string>(type: "TEXT", nullable: false),
                    path = table.Column<string>(type: "TEXT", nullable: false),
                    action = table.Column<int>(type: "INTEGER", nullable: false),
                    reason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_duplicate_resolution_member", x => new { x.resolution_id, x.item_id });
                    table.ForeignKey(
                        name: "FK_duplicate_resolution_member_duplicate_resolution_resolution_id",
                        column: x => x.resolution_id,
                        principalTable: "duplicate_resolution",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_duplicate_resolution_created_at_id",
                table: "duplicate_resolution",
                columns: new[] { "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_duplicate_resolution_group_id",
                table: "duplicate_resolution",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "IX_duplicate_resolution_member_item_id",
                table: "duplicate_resolution_member",
                column: "item_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "duplicate_resolution_member");

            migrationBuilder.DropTable(
                name: "duplicate_resolution");
        }
    }
}
