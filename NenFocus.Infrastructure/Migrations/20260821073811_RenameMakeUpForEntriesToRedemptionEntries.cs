using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NenFocus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameMakeUpForEntriesToRedemptionEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MakeUpForEntries",
                table: "PlannedBlocks",
                newName: "RedemptionEntries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RedemptionEntries",
                table: "PlannedBlocks",
                newName: "MakeUpForEntries");
        }
    }
}
