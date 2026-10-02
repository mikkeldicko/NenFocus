using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NenFocus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ContractRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BonusBlocksFollowStrictness",
                table: "NenContracts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "DailyFloorHours",
                table: "NenContracts",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BonusBlocksFollowStrictness",
                table: "ContractPresets",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // A contract's floor rule is no longer stored — ContractRules works it out from the
            // fixed daily floor and the strictness (Gap 93). Only a fixed rule carried a value
            // of its own; "follows the plan" and "none" both become "no fixed floor".
            migrationBuilder.Sql(
                "UPDATE NenContracts SET DailyFloorHours = " +
                "CASE WHEN json_extract(FloorRule, '$.Kind') = 'Fixed' THEN json_extract(FloorRule, '$.Hours') END;");

            migrationBuilder.DropColumn(
                name: "FloorRule",
                table: "NenContracts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FloorRule",
                table: "NenContracts",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                "UPDATE NenContracts SET FloorRule = CASE " +
                "WHEN DailyFloorHours IS NOT NULL THEN json_object('Kind', 'Fixed', 'Hours', DailyFloorHours) " +
                "WHEN StrictnessRule = 'PeriodTimeBudget' THEN json_object('Kind', 'None', 'Hours', NULL) " +
                "ELSE json_object('Kind', 'FollowsPlan', 'Hours', NULL) END;");

            migrationBuilder.DropColumn(
                name: "BonusBlocksFollowStrictness",
                table: "NenContracts");

            migrationBuilder.DropColumn(
                name: "DailyFloorHours",
                table: "NenContracts");

            migrationBuilder.DropColumn(
                name: "BonusBlocksFollowStrictness",
                table: "ContractPresets");
        }
    }
}
