using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NenFocus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBreakTimerPreset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "BreakLengthMinutes",
                table: "Sessions",
                newName: "TimerWorkMinutes");

            migrationBuilder.RenameColumn(
                name: "BreakIntervalMinutes",
                table: "Sessions",
                newName: "TimerShortBreakMinutes");

            migrationBuilder.AddColumn<string>(
                name: "CurrentBreakKind",
                table: "Sessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TimerCyclesBeforeLongBreak",
                table: "Sessions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TimerLongBreakMinutes",
                table: "Sessions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PauseEnforcementDuringBreak",
                table: "NenContracts",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "PauseEnforcementDuringBreak",
                table: "ContractPresets",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "BreakTimerPresets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false, collation: "NOCASE"),
                    WorkMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    ShortBreakMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    CyclesBeforeLongBreak = table.Column<int>(type: "INTEGER", nullable: false),
                    LongBreakMinutes = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BreakTimerPresets", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "BreakTimerPresets",
                columns: new[] { "Id", "Name", "CyclesBeforeLongBreak", "LongBreakMinutes", "ShortBreakMinutes", "WorkMinutes" },
                values: new object[,]
                {
                    { new Guid("b0000002-0000-0000-0000-000000000001"), "Classic", 4, 15, 5, 25 },
                    { new Guid("b0000002-0000-0000-0000-000000000002"), "Deep Work", 3, 20, 10, 50 },
                    { new Guid("b0000002-0000-0000-0000-000000000003"), "Flow", 2, 30, 20, 90 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_BreakTimerPresets_Name",
                table: "BreakTimerPresets",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BreakTimerPresets");

            migrationBuilder.DropColumn(
                name: "CurrentBreakKind",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "TimerCyclesBeforeLongBreak",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "TimerLongBreakMinutes",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "PauseEnforcementDuringBreak",
                table: "NenContracts");

            migrationBuilder.DropColumn(
                name: "PauseEnforcementDuringBreak",
                table: "ContractPresets");

            migrationBuilder.RenameColumn(
                name: "TimerWorkMinutes",
                table: "Sessions",
                newName: "BreakLengthMinutes");

            migrationBuilder.RenameColumn(
                name: "TimerShortBreakMinutes",
                table: "Sessions",
                newName: "BreakIntervalMinutes");
        }
    }
}
