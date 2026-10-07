using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NenFocus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SessionStorageBackstops : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Sessions_OneActive",
                table: "Sessions",
                column: "Status",
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_OnePerOriginatingBlock",
                table: "Sessions",
                column: "OriginatingBlockId",
                unique: true,
                filter: "\"OriginatingBlockId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sessions_OneActive",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_OnePerOriginatingBlock",
                table: "Sessions");
        }
    }
}
