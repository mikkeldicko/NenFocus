using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NenFocus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class KeepBlockingDuringBreaks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Gap 64: the rule is renamed and inverted — PauseEnforcementDuringBreak (true =
            // permissive) becomes KeepBlockingDuringBreaks (false = permissive). New columns
            // first, values copied across inverted, old columns dropped last, so no
            // contract or preset loses its setting.
            migrationBuilder.AddColumn<bool>(
                name: "KeepBlockingDuringBreaks",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "KeepBlockingDuringBreaks",
                table: "NenContracts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "KeepBlockingDuringBreaks",
                table: "ContractPresets",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("UPDATE NenContracts SET KeepBlockingDuringBreaks = NOT PauseEnforcementDuringBreak;");
            migrationBuilder.Sql("UPDATE ContractPresets SET KeepBlockingDuringBreaks = NOT PauseEnforcementDuringBreak;");

            migrationBuilder.DropColumn(
                name: "PauseEnforcementDuringBreak",
                table: "NenContracts");

            migrationBuilder.DropColumn(
                name: "PauseEnforcementDuringBreak",
                table: "ContractPresets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.Sql("UPDATE NenContracts SET PauseEnforcementDuringBreak = NOT KeepBlockingDuringBreaks;");
            migrationBuilder.Sql("UPDATE ContractPresets SET PauseEnforcementDuringBreak = NOT KeepBlockingDuringBreaks;");

            migrationBuilder.DropColumn(
                name: "KeepBlockingDuringBreaks",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "KeepBlockingDuringBreaks",
                table: "NenContracts");

            migrationBuilder.DropColumn(
                name: "KeepBlockingDuringBreaks",
                table: "ContractPresets");
        }
    }
}
