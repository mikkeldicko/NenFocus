using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NenFocus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNenContractPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MakeUpForEntries",
                table: "PlannedBlocks",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "'[]'");

            migrationBuilder.CreateTable(
                name: "NenContracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Goal = table.Column<string>(type: "TEXT", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    StrictnessRule = table.Column<string>(type: "TEXT", nullable: false),
                    DailyFloorPolicy = table.Column<string>(type: "TEXT", nullable: false),
                    AllowCancellation = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    WorkHoursPolicy = table.Column<string>(type: "TEXT", nullable: true),
                    ContractPresetId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NenContracts", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NenContracts");

            migrationBuilder.DropColumn(
                name: "MakeUpForEntries",
                table: "PlannedBlocks");
        }
    }
}
