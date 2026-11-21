using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaFileClassifier.Infrastructure.DataAccess.Migrations
{
    /// <summary>
    /// Library schema 16: volume labels (MFC-472, E14-S07-T02). Adds <c>volume_label</c> to <c>indexed_folder</c>, the
    /// label of the drive the folder is on, so an Offline folder can name the drive to connect. Existing folders get
    /// <c>NULL</c> until their next availability check while Online.
    /// </summary>
    public partial class VolumeLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "volume_label",
                table: "indexed_folder",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "volume_label",
                table: "indexed_folder");
        }
    }
}
