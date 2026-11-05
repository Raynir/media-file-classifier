using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaFileClassifier.Infrastructure.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class KeyframePerceptualHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "perceptual_hash",
                table: "keyframe",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "perceptual_hash",
                table: "keyframe");
        }
    }
}
