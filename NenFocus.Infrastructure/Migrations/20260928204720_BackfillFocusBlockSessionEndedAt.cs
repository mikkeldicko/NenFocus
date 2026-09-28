using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NenFocus.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillFocusBlockSessionEndedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Gap 86: FocusBlock.SessionEndedAt used to be stamped only when a session ended
            // early. It is now always stamped, so it means the same as Session.EndedAt. Data
            // only, no schema change: fill in the stamp for blocks whose session ran to the
            // end, copied from that session. Only empty stamps are touched.
            migrationBuilder.Sql("""
                UPDATE PlannedBlocks
                SET SessionEndedAt = (
                    SELECT s.EndedAt FROM Sessions s
                    WHERE s.OriginatingBlockId = PlannedBlocks.Id
                      AND s.Status = 'Completed'
                      AND s.EndedAt IS NOT NULL
                    LIMIT 1)
                WHERE BlockKind = 'Focus'
                  AND SessionEndedAt IS NULL
                  AND RitualCompletedAt IS NOT NULL
                  AND EXISTS (
                    SELECT 1 FROM Sessions s
                    WHERE s.OriginatingBlockId = PlannedBlocks.Id
                      AND s.Status = 'Completed'
                      AND s.EndedAt IS NOT NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old rule stored a stamp only when it was before the block's End, so clearing
            // every stamp at or after End restores exactly the previous data.
            migrationBuilder.Sql("""
                UPDATE PlannedBlocks
                SET SessionEndedAt = NULL
                WHERE BlockKind = 'Focus'
                  AND SessionEndedAt IS NOT NULL
                  AND SessionEndedAt >= "End";
                """);
        }
    }
}
