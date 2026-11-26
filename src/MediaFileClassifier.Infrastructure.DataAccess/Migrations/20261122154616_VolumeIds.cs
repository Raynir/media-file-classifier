using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaFileClassifier.Infrastructure.DataAccess.Migrations
{
    /// <summary>
    /// Library schema 18: volume identities (MFC-718, E04-S04-T03). Adds <c>volume_id</c> to <c>indexed_folder</c>, the
    /// identity of the drive the folder is on (on Windows its file system's serial number), so a drive that comes back
    /// under another letter is found again and another drive that took the letter isn't taken for it. Existing folders
    /// get <c>NULL</c> until their next availability check while Online.
    /// </summary>
    public partial class VolumeIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "volume_id",
                table: "indexed_folder",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "volume_id",
                table: "indexed_folder");
        }
    }
}
