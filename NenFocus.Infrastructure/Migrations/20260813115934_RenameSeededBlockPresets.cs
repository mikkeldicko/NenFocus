using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NenFocus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameSeededBlockPresets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "BlockPresets",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000001"),
                column: "Name",
                value: "AI & Dev Tools Only");

            migrationBuilder.UpdateData(
                table: "BlockPresets",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000002"),
                column: "Name",
                value: "Social Media Blocked");

            migrationBuilder.UpdateData(
                table: "BlockPresets",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000003"),
                column: "Name",
                value: "Social Media Blocked");

            migrationBuilder.UpdateData(
                table: "BlockPresets",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000004"),
                column: "Name",
                value: "Work & Music Only");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "BlockPresets",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000001"),
                column: "Name",
                value: "Focus — Allowlist");

            migrationBuilder.UpdateData(
                table: "BlockPresets",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000002"),
                column: "Name",
                value: "Focus — Blocklist");

            migrationBuilder.UpdateData(
                table: "BlockPresets",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000003"),
                column: "Name",
                value: "Background — Blocklist");

            migrationBuilder.UpdateData(
                table: "BlockPresets",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000004"),
                column: "Name",
                value: "Background — Allowlist");
        }
    }
}
