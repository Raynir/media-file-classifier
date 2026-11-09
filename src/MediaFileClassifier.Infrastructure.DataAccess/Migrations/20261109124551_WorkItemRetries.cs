using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaFileClassifier.Infrastructure.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class WorkItemRetries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "error_category",
                table: "work_item",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "retry_at",
                table: "work_item",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "retry_count",
                table: "work_item",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "error_category",
                table: "work_item");

            migrationBuilder.DropColumn(
                name: "retry_at",
                table: "work_item");

            migrationBuilder.DropColumn(
                name: "retry_count",
                table: "work_item");
        }
    }
}
