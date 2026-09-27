using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NenFocus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TimeDrivenBreakSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A break is no longer a status (Gap 63) — a session saved mid-break is simply
            // a running session. Its old break history can't be reconstructed and is dropped
            // with the columns below; no rows are deleted.
            migrationBuilder.Sql("UPDATE Sessions SET Status = 'Active' WHERE Status = 'OnBreak';");

            migrationBuilder.DropColumn(
                name: "BreakStartedAt",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "BreaksTaken",
                table: "Sessions");

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
                name: "TimerShortBreakMinutes",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "TimerWorkMinutes",
                table: "Sessions");

            migrationBuilder.AddColumn<string>(
                name: "BreakPresetName",
                table: "Sessions",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SessionBreaks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Start = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    End = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionBreaks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionBreaks_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionBreaks_SessionId",
                table: "SessionBreaks",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionBreaks");

            migrationBuilder.DropColumn(
                name: "BreakPresetName",
                table: "Sessions");

            migrationBuilder.AddColumn<DateTime>(
                name: "BreakStartedAt",
                table: "Sessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BreaksTaken",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

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

            migrationBuilder.AddColumn<int>(
                name: "TimerShortBreakMinutes",
                table: "Sessions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TimerWorkMinutes",
                table: "Sessions",
                type: "INTEGER",
                nullable: true);
        }
    }
}
