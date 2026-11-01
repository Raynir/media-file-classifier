using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaFileClassifier.Infrastructure.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class TechnicalMetadataDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "color_space",
                table: "technical_metadata",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "date_taken_source",
                table: "technical_metadata",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "has_audio",
                table: "technical_metadata",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "loop_count",
                table: "technical_metadata",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "warnings",
                table: "technical_metadata",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "color_space",
                table: "technical_metadata");

            migrationBuilder.DropColumn(
                name: "date_taken_source",
                table: "technical_metadata");

            migrationBuilder.DropColumn(
                name: "has_audio",
                table: "technical_metadata");

            migrationBuilder.DropColumn(
                name: "loop_count",
                table: "technical_metadata");

            migrationBuilder.DropColumn(
                name: "warnings",
                table: "technical_metadata");
        }
    }
}
