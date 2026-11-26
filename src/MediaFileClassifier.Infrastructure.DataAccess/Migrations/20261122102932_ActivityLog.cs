using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaFileClassifier.Infrastructure.DataAccess.Migrations
{
    /// <summary>
    /// Library schema 17: the activity log (MFC-858, E15-S04-T01). Adds <c>activity_event</c>, which holds the newest
    /// 5,000 significant events of the library. Existing libraries start with an empty log.
    /// </summary>
    public partial class ActivityLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "activity_event",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    type = table.Column<int>(type: "INTEGER", nullable: false),
                    severity = table.Column<int>(type: "INTEGER", nullable: false),
                    occurred_at = table.Column<long>(type: "INTEGER", nullable: false),
                    subject = table.Column<string>(type: "TEXT", nullable: true),
                    counts = table.Column<string>(type: "TEXT", nullable: false),
                    folder_id = table.Column<string>(type: "TEXT", nullable: true),
                    item_id = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activity_event", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_activity_event_occurred_at_id",
                table: "activity_event",
                columns: new[] { "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_activity_event_type_occurred_at",
                table: "activity_event",
                columns: new[] { "type", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_event");
        }
    }
}
