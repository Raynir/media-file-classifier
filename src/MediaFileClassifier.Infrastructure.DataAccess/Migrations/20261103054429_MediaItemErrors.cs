using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaFileClassifier.Infrastructure.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class MediaItemErrors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "error_exception_type",
                table: "media_item",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "error_is_permanent",
                table: "media_item",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "error_stage",
                table: "media_item",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "error_exception_type",
                table: "media_item");

            migrationBuilder.DropColumn(
                name: "error_is_permanent",
                table: "media_item");

            migrationBuilder.DropColumn(
                name: "error_stage",
                table: "media_item");
        }
    }
}
