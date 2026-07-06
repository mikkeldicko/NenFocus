using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NenFocus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BlockedAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResourceName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    AttemptType = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockedAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BlockPresets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    WebsiteFilterType = table.Column<string>(type: "TEXT", nullable: false),
                    PresetType = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Focus"),
                    Tags = table.Column<string>(type: "TEXT", nullable: true, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockPresets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlannedBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BlockPresetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Start = table.Column<DateTime>(type: "TEXT", nullable: false),
                    End = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ContractId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BlockKind = table.Column<string>(type: "TEXT", maxLength: 13, nullable: false),
                    RitualCompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannedBlocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Goal = table.Column<string>(type: "TEXT", nullable: false),
                    DurationMinutes = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Origin = table.Column<string>(type: "TEXT", nullable: false),
                    BlockPresetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginatingBlockId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    BreakIntervalMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    BreakLengthMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    BreaksTaken = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    BreakStartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Reflection = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    TemporaryBlockedApps = table.Column<string>(type: "TEXT", nullable: false, defaultValueSql: "'[]'"),
                    TemporaryBlockedSites = table.Column<string>(type: "TEXT", nullable: false, defaultValueSql: "'[]'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BlockedApps",
                columns: table => new
                {
                    ProcessName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BlockPresetId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockedApps", x => new { x.BlockPresetId, x.ProcessName });
                    table.ForeignKey(
                        name: "FK_BlockedApps_BlockPresets_BlockPresetId",
                        column: x => x.BlockPresetId,
                        principalTable: "BlockPresets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BlockedSites",
                columns: table => new
                {
                    Url = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    BlockPresetId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockedSites", x => new { x.BlockPresetId, x.Url });
                    table.ForeignKey(
                        name: "FK_BlockedSites_BlockPresets_BlockPresetId",
                        column: x => x.BlockPresetId,
                        principalTable: "BlockPresets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "BlockPresets",
                columns: new[] { "Id", "Name", "Tags", "WebsiteFilterType" },
                values: new object[,]
                {
                    { new Guid("a0000001-0000-0000-0000-000000000001"), "Focus — Allowlist", "", "Allowlist" },
                    { new Guid("a0000001-0000-0000-0000-000000000002"), "Focus — Blocklist", "", "Blocklist" }
                });

            migrationBuilder.InsertData(
                table: "BlockPresets",
                columns: new[] { "Id", "Name", "PresetType", "Tags", "WebsiteFilterType" },
                values: new object[,]
                {
                    { new Guid("a0000001-0000-0000-0000-000000000003"), "Background — Blocklist", "Background", "", "Blocklist" },
                    { new Guid("a0000001-0000-0000-0000-000000000004"), "Background — Allowlist", "Background", "", "Allowlist" }
                });

            migrationBuilder.InsertData(
                table: "BlockedSites",
                columns: new[] { "BlockPresetId", "Url" },
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

            migrationBuilder.CreateIndex(
                name: "IX_BlockedAttempts_OccurredAt",
                table: "BlockedAttempts",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_BlockedAttempts_SessionId",
                table: "BlockedAttempts",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlockedApps");

            migrationBuilder.DropTable(
                name: "BlockedAttempts");

            migrationBuilder.DropTable(
                name: "BlockedSites");

            migrationBuilder.DropTable(
                name: "PlannedBlocks");

            migrationBuilder.DropTable(
                name: "Sessions");

            migrationBuilder.DropTable(
                name: "BlockPresets");
        }
    }
}
