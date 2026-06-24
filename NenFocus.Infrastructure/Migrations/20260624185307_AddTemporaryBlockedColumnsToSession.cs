using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NenFocus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTemporaryBlockedColumnsToSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE Sessions ADD COLUMN TemporaryBlockedApps TEXT NOT NULL DEFAULT '[]'");
            migrationBuilder.Sql("ALTER TABLE Sessions ADD COLUMN TemporaryBlockedSites TEXT NOT NULL DEFAULT '[]'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TemporaryBlockedApps",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "TemporaryBlockedSites",
                table: "Sessions");
        }
    }
}
