using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NenFocus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBlockProfileType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE PlannedBlocks SET BlockKind = 'Focus' WHERE BlockKind = 'Ritual'");

            migrationBuilder.AddColumn<string>(
                name: "ProfileType",
                table: "BlockProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "Focus");

            migrationBuilder.InsertData(
                table: "BlockProfiles",
                columns: new[] { "Id", "IsActive", "Name", "Tags", "WebsitePolicy" },
                values: new object[,]
                {
                    { new Guid("a0000001-0000-0000-0000-000000000001"), true, "Focus — Allowlist", "", "Allowlist" },
                    { new Guid("a0000001-0000-0000-0000-000000000002"), true, "Focus — Blocklist", "", "Blocklist" }
                });

            migrationBuilder.InsertData(
                table: "BlockProfiles",
                columns: new[] { "Id", "IsActive", "Name", "ProfileType", "Tags", "WebsitePolicy" },
                values: new object[,]
                {
                    { new Guid("a0000001-0000-0000-0000-000000000003"), true, "Background — Blocklist", "Background", "", "Blocklist" },
                    { new Guid("a0000001-0000-0000-0000-000000000004"), true, "Background — Allowlist", "Background", "", "Allowlist" }
                });

            migrationBuilder.InsertData(
                table: "BlockedSites",
                columns: new[] { "BlockProfileId", "Url" },
                values: new object[,]
                {
                    { new Guid("a0000001-0000-0000-0000-000000000001"), "claude.ai" },
                    { new Guid("a0000001-0000-0000-0000-000000000001"), "gemini.google.com" },
                    { new Guid("a0000001-0000-0000-0000-000000000001"), "github.com" },
                    { new Guid("a0000001-0000-0000-0000-000000000002"), "facebook.com" },
                    { new Guid("a0000001-0000-0000-0000-000000000002"), "instagram.com" },
                    { new Guid("a0000001-0000-0000-0000-000000000002"), "reddit.com" },
                    { new Guid("a0000001-0000-0000-0000-000000000002"), "twitter.com" },
                    { new Guid("a0000001-0000-0000-0000-000000000002"), "youtube.com" },
                    { new Guid("a0000001-0000-0000-0000-000000000003"), "facebook.com" },
                    { new Guid("a0000001-0000-0000-0000-000000000003"), "instagram.com" },
                    { new Guid("a0000001-0000-0000-0000-000000000003"), "reddit.com" },
                    { new Guid("a0000001-0000-0000-0000-000000000003"), "twitter.com" },
                    { new Guid("a0000001-0000-0000-0000-000000000003"), "youtube.com" },
                    { new Guid("a0000001-0000-0000-0000-000000000004"), "claude.ai" },
                    { new Guid("a0000001-0000-0000-0000-000000000004"), "gemini.google.com" },
                    { new Guid("a0000001-0000-0000-0000-000000000004"), "github.com" },
                    { new Guid("a0000001-0000-0000-0000-000000000004"), "google.com" },
                    { new Guid("a0000001-0000-0000-0000-000000000004"), "mail.google.com" },
                    { new Guid("a0000001-0000-0000-0000-000000000004"), "soundcloud.com" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "BlockedSites",
                keyColumns: new[] { "BlockProfileId", "Url" },
                keyValues: new object[] { new Guid("a0000001-0000-0000-0000-000000000001"), "claude.ai" });

            migrationBuilder.DeleteData(
                table: "BlockedSites",
                keyColumns: new[] { "BlockProfileId", "Url" },
                keyValues: new object[] { new Guid("a0000001-0000-0000-0000-000000000001"), "gemini.google.com" });

            migrationBuilder.DeleteData(
                table: "BlockedSites",
                keyColumns: new[] { "BlockProfileId", "Url" },
                keyValues: new object[] { new Guid("a0000001-0000-0000-0000-000000000001"), "github.com" });

            migrationBuilder.DeleteData(
                table: "BlockedSites",
                keyColumns: new[] { "BlockProfileId", "Url" },
                keyValues: new object[] { new Guid("a0000001-0000-0000-0000-000000000002"), "facebook.com" });

            migrationBuilder.DeleteData(
                table: "BlockedSites",
                keyColumns: new[] { "BlockProfileId", "Url" },
                keyValues: new object[] { new Guid("a0000001-0000-0000-0000-000000000002"), "instagram.com" });

            migrationBuilder.DeleteData(
                table: "BlockedSites",
                keyColumns: new[] { "BlockProfileId", "Url" },
                keyValues: new object[] { new Guid("a0000001-0000-0000-0000-000000000002"), "reddit.com" });

            migrationBuilder.DeleteData(
                table: "BlockedSites",
                keyColumns: new[] { "BlockProfileId", "Url" },
                keyValues: new object[] { new Guid("a0000001-0000-0000-0000-000000000002"), "twitter.com" });

            migrationBuilder.DeleteData(
                table: "BlockedSites",
                keyColumns: new[] { "BlockProfileId", "Url" },
                keyValues: new object[] { new Guid("a0000001-0000-0000-0000-000000000002"), "youtube.com" });

            migrationBuilder.DeleteData(
                table: "BlockedSites",
                keyColumns: new[] { "BlockProfileId", "Url" },
                keyValues: new object[] { new Guid("a0000001-0000-0000-0000-000000000003"), "facebook.com" });

            migrationBuilder.DeleteData(
                table: "BlockedSites",
                keyColumns: new[] { "BlockProfileId", "Url" },
                keyValues: new object[] { new Guid("a0000001-0000-0000-0000-000000000003"), "instagram.com" });

            migrationBuilder.DeleteData(
                table: "BlockedSites",
                keyColumns: new[] { "BlockProfileId", "Url" },
                keyValues: new object[] { new Guid("a0000001-0000-0000-0000-000000000003"), "reddit.com" });

            migrationBuilder.DeleteData(
                table: "BlockedSites",
                keyColumns: new[] { "BlockProfileId", "Url" },
                keyValues: new object[] { new Guid("a0000001-0000-0000-0000-000000000003"), "twitter.com" });

            migrationBuilder.DeleteData(
                table: "BlockedSites",
                keyColumns: new[] { "BlockProfileId", "Url" },
                keyValues: new object[] { new Guid("a0000001-0000-0000-0000-000000000003"), "youtube.com" });

            migrationBuilder.DeleteData(
                table: "BlockedSites",
                keyColumns: new[] { "BlockProfileId", "Url" },
                keyValues: new object[] { new Guid("a0000001-0000-0000-0000-000000000004"), "claude.ai" });

            migrationBuilder.DeleteData(
                table: "BlockedSites",
                keyColumns: new[] { "BlockProfileId", "Url" },
                keyValues: new object[] { new Guid("a0000001-0000-0000-0000-000000000004"), "gemini.google.com" });

            migrationBuilder.DeleteData(
                table: "BlockedSites",
                keyColumns: new[] { "BlockProfileId", "Url" },
                keyValues: new object[] { new Guid("a0000001-0000-0000-0000-000000000004"), "github.com" });

            migrationBuilder.DeleteData(
                table: "BlockedSites",
                keyColumns: new[] { "BlockProfileId", "Url" },
                keyValues: new object[] { new Guid("a0000001-0000-0000-0000-000000000004"), "google.com" });

            migrationBuilder.DeleteData(
                table: "BlockedSites",
                keyColumns: new[] { "BlockProfileId", "Url" },
                keyValues: new object[] { new Guid("a0000001-0000-0000-0000-000000000004"), "mail.google.com" });

            migrationBuilder.DeleteData(
                table: "BlockedSites",
                keyColumns: new[] { "BlockProfileId", "Url" },
                keyValues: new object[] { new Guid("a0000001-0000-0000-0000-000000000004"), "soundcloud.com" });

            migrationBuilder.DeleteData(
                table: "BlockProfiles",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "BlockProfiles",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "BlockProfiles",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "BlockProfiles",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0000-0000-0000-000000000004"));

            migrationBuilder.DropColumn(
                name: "ProfileType",
                table: "BlockProfiles");

            migrationBuilder.Sql("UPDATE PlannedBlocks SET BlockKind = 'Ritual' WHERE BlockKind = 'Focus'");
        }
    }
}
