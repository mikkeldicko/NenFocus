using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

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
                name: "BlockProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    WebsitePolicy = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    Tags = table.Column<string>(type: "TEXT", nullable: true, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlannedBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BlockProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Start = table.Column<DateTime>(type: "TEXT", nullable: false),
                    End = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
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
                    BlockProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginatingBlockId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    BreakIntervalMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    BreakLengthMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    BreaksTaken = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    BreakStartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TemporaryBlockedApps = table.Column<string>(type: "TEXT", nullable: false, defaultValueSql: "'[]'"),
                    TemporaryBlockedSites = table.Column<string>(type: "TEXT", nullable: false, defaultValueSql: "'[]'"),
                    Reflection = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Rating = table.Column<int>(type: "INTEGER", nullable: true),
                    BlockedAttempts = table.Column<string>(type: "TEXT", nullable: true)
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
                    BlockProfileId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockedApps", x => new { x.BlockProfileId, x.ProcessName });
                    table.ForeignKey(
                        name: "FK_BlockedApps_BlockProfiles_BlockProfileId",
                        column: x => x.BlockProfileId,
                        principalTable: "BlockProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BlockedSites",
                columns: table => new
                {
                    Url = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    BlockProfileId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockedSites", x => new { x.BlockProfileId, x.Url });
                    table.ForeignKey(
                        name: "FK_BlockedSites_BlockProfiles_BlockProfileId",
                        column: x => x.BlockProfileId,
                        principalTable: "BlockProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "BlockProfiles");
        }
    }
}
