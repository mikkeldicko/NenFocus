using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NenFocus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ContractMembershipRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContractRole",
                table: "PlannedBlocks",
                type: "TEXT",
                nullable: true);

            // Gap 97: every block that already carries a contract id gets its role — covering a
            // deficit means Redemption, anything else was drafted, so Committed. Every such row
            // must be reached: an id without a role loads as no membership at all.
            migrationBuilder.Sql("""
                UPDATE PlannedBlocks
                SET ContractRole = CASE
                    WHEN RedemptionEntries IS NOT NULL AND RedemptionEntries NOT IN ('', '[]') THEN 'Redemption'
                    ELSE 'Committed' END
                WHERE ContractId IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContractRole",
                table: "PlannedBlocks");
        }
    }
}
