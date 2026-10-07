using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NenFocus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoveRedemptionReceiptToFocusBlock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RedemptionEntries",
                table: "PlannedBlocks",
                type: "TEXT",
                nullable: true,
                defaultValueSql: "'[]'",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldDefaultValueSql: "'[]'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RedemptionEntries",
                table: "PlannedBlocks",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "'[]'",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true,
                oldDefaultValueSql: "'[]'");
        }
    }
}
