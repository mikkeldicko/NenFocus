using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NenFocus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNenContractOriginalEndDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "OriginalEndDate",
                table: "NenContracts",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            // Backfill for contracts that already existed before this column did. For a
            // contract that was never broken, its current EndDate already *is* its original
            // end date (Break is the only thing that ever changes EndDate away from what
            // Seal set) — so copying EndDate is correct, not just a placeholder, for every
            // pre-existing row. A contract that HAD already been broken before this migration
            // runs loses its true original end date (it was never recorded until now) and
            // backfills to its already-shrunk EndDate instead — an accepted, unavoidable gap
            // for data that predates this feature, not a bug in the backfill itself.
            migrationBuilder.Sql("UPDATE NenContracts SET OriginalEndDate = EndDate;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginalEndDate",
                table: "NenContracts");
        }
    }
}
