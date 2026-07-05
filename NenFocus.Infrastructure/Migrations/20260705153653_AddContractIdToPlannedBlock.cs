using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NenFocus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContractIdToPlannedBlock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ContractId",
                table: "PlannedBlocks",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContractId",
                table: "PlannedBlocks");
        }
    }
}
