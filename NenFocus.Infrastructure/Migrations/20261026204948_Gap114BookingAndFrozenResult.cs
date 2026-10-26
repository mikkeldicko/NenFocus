using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NenFocus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Gap114BookingAndFrozenResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlockPresetId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "DurationMinutes",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "SessionEndedAt",
                table: "Blocks");

            migrationBuilder.AlterColumn<Guid>(
                name: "OriginatingBlockId",
                table: "Sessions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BookingType",
                table: "Blocks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PostSessionAcknowledgedAt",
                table: "Blocks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reflection",
                table: "Blocks",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultOutcome",
                table: "Blocks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResultWorkStart",
                table: "Blocks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResultWorkEnd",
                table: "Blocks",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BookingType",
                table: "Blocks");

            migrationBuilder.DropColumn(
                name: "PostSessionAcknowledgedAt",
                table: "Blocks");

            migrationBuilder.DropColumn(
                name: "Reflection",
                table: "Blocks");

            migrationBuilder.DropColumn(
                name: "ResultOutcome",
                table: "Blocks");

            migrationBuilder.DropColumn(
                name: "ResultWorkEnd",
                table: "Blocks");

            migrationBuilder.DropColumn(
                name: "ResultWorkStart",
                table: "Blocks");

            migrationBuilder.AddColumn<DateTime>(
                name: "SessionEndedAt",
                table: "Blocks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "OriginatingBlockId",
                table: "Sessions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<Guid>(
                name: "BlockPresetId",
                table: "Sessions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<long>(
                name: "DurationMinutes",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "Sessions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
