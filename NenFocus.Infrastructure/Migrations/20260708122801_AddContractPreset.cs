using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NenFocus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContractPreset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContractPresets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    StrictnessRule = table.Column<string>(type: "TEXT", nullable: false),
                    DailyFloorHours = table.Column<double>(type: "REAL", nullable: false),
                    WorkHoursStart = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                    WorkHoursEnd = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                    AllowCancellation = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractPresets", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractPresets");
        }
    }
}
