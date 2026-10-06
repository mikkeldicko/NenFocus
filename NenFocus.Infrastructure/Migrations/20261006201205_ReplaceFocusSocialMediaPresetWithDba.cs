using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NenFocus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceFocusSocialMediaPresetWithDba : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "BlockedSites",
                keyColumns: new[] { "BlockPresetId", "Url" },
                keyValues: new object[] { new Guid("a0000001-0000-0000-0000-000000000002"), "facebook.com" });

            migrationBuilder.DeleteData(
                table: "BlockedSites",
                keyColumns: new[] { "BlockPresetId", "Url" },
                keyValues: new object[] { new Guid("a0000001-0000-0000-0000-000000000002"), "instagram.com" });

            migrationBuilder.DeleteData(
                table: "BlockedSites",
                keyColumns: new[] { "BlockPresetId", "Url" },
                keyValues: new object[] { new Guid("a0000001-0000-0000-0000-000000000002"), "reddit.com" });

            migrationBuilder.DeleteData(
                table: "BlockedSites",
                keyColumns: new[] { "BlockPresetId", "Url" },
                keyValues: new object[] { new Guid("a0000001-0000-0000-0000-000000000002"), "twitter.com" });

            migrationBuilder.DeleteData(
                table: "BlockedSites",
                keyColumns: new[] { "BlockPresetId", "Url" },
                keyValues: new object[] { new Guid("a0000001-0000-0000-0000-000000000002"), "youtube.com" });

            migrationBuilder.UpdateData(
                table: "BlockPresets",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000002"),
                column: "Name",
                value: "DBA block");

            migrationBuilder.InsertData(
                table: "BlockedSites",
                columns: new[] { "BlockPresetId", "Url" },
                values: new object[] { new Guid("a0000001-0000-0000-0000-000000000002"), "dba.dk" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "BlockedSites",
                keyColumns: new[] { "BlockPresetId", "Url" },
                keyValues: new object[] { new Guid("a0000001-0000-0000-0000-000000000002"), "dba.dk" });

            migrationBuilder.UpdateData(
                table: "BlockPresets",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000002"),
                column: "Name",
                value: "Social Media Blocked");

            migrationBuilder.InsertData(
                table: "BlockedSites",
                columns: new[] { "BlockPresetId", "Url" },
                values: new object[,]
                {
                    { new Guid("a0000001-0000-0000-0000-000000000002"), "facebook.com" },
                    { new Guid("a0000001-0000-0000-0000-000000000002"), "instagram.com" },
                    { new Guid("a0000001-0000-0000-0000-000000000002"), "reddit.com" },
                    { new Guid("a0000001-0000-0000-0000-000000000002"), "twitter.com" },
                    { new Guid("a0000001-0000-0000-0000-000000000002"), "youtube.com" }
                });
        }
    }
}
