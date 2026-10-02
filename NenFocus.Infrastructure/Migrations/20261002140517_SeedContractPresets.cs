using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NenFocus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedContractPresets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "ContractPresets",
                columns: new[] { "Id", "AllowCancellation", "DailyFloorHours", "KeepBlockingDuringBreaks", "Name", "RedemptionPlacementPolicy", "StrictnessRule", "WorkHoursEnd", "WorkHoursStart" },
                values: new object[] { new Guid("c0000003-0000-0000-0000-000000000001"), false, 4.0, true, "Lockdown", "AnywhereInContract", "Locked", new TimeOnly(17, 0, 0), new TimeOnly(9, 0, 0) });

            migrationBuilder.InsertData(
                table: "ContractPresets",
                columns: new[] { "Id", "AllowCancellation", "DailyFloorHours", "Name", "RedemptionPlacementPolicy", "StrictnessRule", "WorkHoursEnd", "WorkHoursStart" },
                values: new object[,]
                {
                    { new Guid("c0000003-0000-0000-0000-000000000002"), true, null, "Daily Flex", "AnywhereInContract", "DailyFlexibility", new TimeOnly(20, 0, 0), new TimeOnly(8, 0, 0) },
                    { new Guid("c0000003-0000-0000-0000-000000000003"), true, null, "Period Study", "AnywhereInContract", "PeriodTimeBudget", new TimeOnly(23, 0, 0), new TimeOnly(7, 0, 0) }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ContractPresets",
                keyColumn: "Id",
                keyValue: new Guid("c0000003-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "ContractPresets",
                keyColumn: "Id",
                keyValue: new Guid("c0000003-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "ContractPresets",
                keyColumn: "Id",
                keyValue: new Guid("c0000003-0000-0000-0000-000000000003"));
        }
    }
}
