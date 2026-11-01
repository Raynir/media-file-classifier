using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaFileClassifier.Infrastructure.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class FavoriteAndReviewChangeTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "favorite_changed_at",
                table: "media_item",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "review_status_changed_at",
                table: "media_item",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "favorite_changed_at",
                table: "media_item");

            migrationBuilder.DropColumn(
                name: "review_status_changed_at",
                table: "media_item");
        }
    }
}
