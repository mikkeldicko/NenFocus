using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NenFocus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenamePlannedBlocksToBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_PlannedBlocks",
                table: "PlannedBlocks");

            migrationBuilder.RenameTable(
                name: "PlannedBlocks",
                newName: "Blocks");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Blocks",
                table: "Blocks",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Blocks",
                table: "Blocks");

            migrationBuilder.RenameTable(
                name: "Blocks",
                newName: "PlannedBlocks");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PlannedBlocks",
                table: "PlannedBlocks",
                column: "Id");
        }
    }
}
