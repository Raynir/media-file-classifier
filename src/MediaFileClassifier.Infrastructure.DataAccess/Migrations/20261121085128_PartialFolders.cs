using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaFileClassifier.Infrastructure.DataAccess.Migrations
{
    /// <summary>
    /// Library schema 15: partial folders (MFC-651, D012). Adds <c>selected_files_only</c> to <c>indexed_folder</c>, set
    /// for a folder that tracks only the files the user selected in it. Existing folders get 0 and stay full folders.
    /// </summary>
    public partial class PartialFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "selected_files_only",
                table: "indexed_folder",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "selected_files_only",
                table: "indexed_folder");
        }
    }
}
