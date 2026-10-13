using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NenFocus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBlockPresetTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Tags",
                table: "BlockPresets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "BlockPresets",
                type: "TEXT",
                nullable: true,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "BlockPresets",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000001"),
                column: "Tags",
                value: "");

            migrationBuilder.UpdateData(
                table: "BlockPresets",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000002"),
                column: "Tags",
                value: "");

            migrationBuilder.UpdateData(
                table: "BlockPresets",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000003"),
                column: "Tags",
                value: "");

            migrationBuilder.UpdateData(
                table: "BlockPresets",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000004"),
                column: "Tags",
                value: "");
        }
    }
}
