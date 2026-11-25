using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaFileClassifier.Infrastructure.DataAccess.Migrations
{
    /// <summary>
    /// Library schema 19: monitoring modes (MFC-719, E04-S05-T01). Adds <c>monitoring_mode</c> to <c>indexed_folder</c>:
    /// 0 Auto (Poll on a network path, Watch otherwise), 1 Watch, 2 Poll, 3 Manual. Existing folders get Auto, so they
    /// keep being watched unless they're on a network path.
    /// </summary>
    public partial class MonitoringModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "monitoring_mode",
                table: "indexed_folder",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "monitoring_mode",
                table: "indexed_folder");
        }
    }
}
