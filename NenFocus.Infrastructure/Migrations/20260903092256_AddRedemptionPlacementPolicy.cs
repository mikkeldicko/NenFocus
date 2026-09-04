using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NenFocus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRedemptionPlacementPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RedemptionPlacementPolicy",
                table: "NenContracts",
                type: "TEXT",
                nullable: false,
                defaultValue: "AnywhereInContract");

            migrationBuilder.AddColumn<string>(
                name: "RedemptionPlacementPolicy",
                table: "ContractPresets",
                type: "TEXT",
                nullable: false,
                defaultValue: "AnywhereInContract");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RedemptionPlacementPolicy",
                table: "NenContracts");

            migrationBuilder.DropColumn(
                name: "RedemptionPlacementPolicy",
                table: "ContractPresets");
        }
    }
}
