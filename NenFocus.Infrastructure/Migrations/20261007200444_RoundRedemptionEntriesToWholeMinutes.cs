using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NenFocus.Infrastructure.Migrations
{
    /// <summary>
    /// Gap 92 (V10): a redemption entry covers whole minutes, stored as a JSON integer. Older rows
    /// may hold a fraction (31.9); each is rounded up, so stored coverage is never less than it was.
    /// SQLite's ceil() needs the math extension, so it is spelled out: the integer part, plus one
    /// when there was a remainder. No schema change.
    /// </summary>
    public partial class RoundRedemptionEntriesToWholeMinutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE PlannedBlocks
                SET RedemptionEntries = (
                    SELECT json_group_array(json_object(
                        'MissedBlockId', json_extract(e.value, '$.MissedBlockId'),
                        'MinutesCovered',
                            CAST(json_extract(e.value, '$.MinutesCovered') AS INTEGER)
                            + (json_extract(e.value, '$.MinutesCovered') > CAST(json_extract(e.value, '$.MinutesCovered') AS INTEGER))))
                    FROM json_each(PlannedBlocks.RedemptionEntries) AS e)
                WHERE RedemptionEntries IS NOT NULL AND json_array_length(RedemptionEntries) > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Whole numbers are valid fractional values too; nothing to undo.
        }
    }
}
