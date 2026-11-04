using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaFileClassifier.Infrastructure.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class WorkItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "work_item",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    job_id = table.Column<string>(type: "TEXT", nullable: false),
                    item_id = table.Column<string>(type: "TEXT", nullable: true),
                    stage = table.Column<string>(type: "TEXT", nullable: false),
                    pool = table.Column<int>(type: "INTEGER", nullable: false),
                    priority = table.Column<int>(type: "INTEGER", nullable: false),
                    state = table.Column<int>(type: "INTEGER", nullable: false),
                    attempt_count = table.Column<int>(type: "INTEGER", nullable: false),
                    last_error = table.Column<string>(type: "TEXT", nullable: true),
                    payload = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    started_at = table.Column<long>(type: "INTEGER", nullable: true),
                    finished_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_item", x => x.id);
                    table.ForeignKey(
                        name: "FK_work_item_media_item_item_id",
                        column: x => x.item_id,
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_work_item_processing_job_job_id",
                        column: x => x.job_id,
                        principalTable: "processing_job",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_work_item_item_id",
                table: "work_item",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "IX_work_item_job_id_state",
                table: "work_item",
                columns: new[] { "job_id", "state" });

            migrationBuilder.CreateIndex(
                name: "IX_work_item_state_pool_priority_created_at",
                table: "work_item",
                columns: new[] { "state", "pool", "priority", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "work_item");
        }
    }
}
