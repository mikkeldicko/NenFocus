using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NenFocus.Infrastructure.Migrations
{
    public partial class RenameSessionOriginToSpontaneous : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Sessions SET Origin = 'Spontaneous' WHERE Origin = 'Impromptu'");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Sessions SET Origin = 'Impromptu' WHERE Origin = 'Spontaneous'");
        }
    }
}
