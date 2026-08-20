using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NenFocus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPresetNameUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ContractPresets",
                type: "TEXT",
                maxLength: 60,
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 60);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "BlockPresets",
                type: "TEXT",
                maxLength: 60,
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 60);

            migrationBuilder.CreateIndex(
                name: "IX_ContractPresets_Name",
                table: "ContractPresets",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlockPresets_Name_PresetType",
                table: "BlockPresets",
                columns: new[] { "Name", "PresetType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ContractPresets_Name",
                table: "ContractPresets");

            migrationBuilder.DropIndex(
                name: "IX_BlockPresets_Name_PresetType",
                table: "BlockPresets");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ContractPresets",
                type: "TEXT",
                maxLength: 60,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 60,
                oldCollation: "NOCASE");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "BlockPresets",
                type: "TEXT",
                maxLength: 60,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 60,
                oldCollation: "NOCASE");
        }
    }
}
