using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NenFocus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ContractDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Gap 65: a contract stores its days (window + floor per date) and its floor rule
            // instead of WorkHoursPolicy/DailyFloorPolicy. Schema only — existing contracts are
            // not converted (the local database is wiped before testing this change).
            migrationBuilder.AddColumn<string>(
                name: "Days",
                table: "NenContracts",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "FloorRule",
                table: "NenContracts",
                type: "TEXT",
                nullable: false,
                defaultValue: "{\"Kind\":\"None\",\"Hours\":null}");

            migrationBuilder.DropColumn(
                name: "WorkHoursPolicy",
                table: "NenContracts");

            migrationBuilder.DropColumn(
                name: "DailyFloorPolicy",
                table: "NenContracts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DailyFloorPolicy",
                table: "NenContracts",
                type: "TEXT",
                nullable: false,
                defaultValue: "{\"Default\":null,\"PerDayExceptions\":{}}");

            migrationBuilder.AddColumn<string>(
                name: "WorkHoursPolicy",
                table: "NenContracts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.DropColumn(
                name: "Days",
                table: "NenContracts");

            migrationBuilder.DropColumn(
                name: "FloorRule",
                table: "NenContracts");
        }
    }
}
