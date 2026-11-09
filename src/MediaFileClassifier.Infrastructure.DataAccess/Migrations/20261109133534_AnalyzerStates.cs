using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaFileClassifier.Infrastructure.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AnalyzerStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "analyzer_state",
                columns: table => new
                {
                    item_id = table.Column<string>(type: "TEXT", nullable: false),
                    analyzer = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    reason_code = table.Column<string>(type: "TEXT", nullable: true),
                    detail = table.Column<string>(type: "TEXT", nullable: true),
                    failure_kind = table.Column<string>(type: "TEXT", nullable: true),
                    is_retryable = table.Column<bool>(type: "INTEGER", nullable: false),
                    run_id = table.Column<string>(type: "TEXT", nullable: true),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analyzer_state", x => new { x.item_id, x.analyzer });
                    table.ForeignKey(
                        name: "FK_analyzer_state_media_item_item_id",
                        column: x => x.item_id,
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_analyzer_state_analyzer_status",
                table: "analyzer_state",
                columns: new[] { "analyzer", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "analyzer_state");
        }
    }
}
