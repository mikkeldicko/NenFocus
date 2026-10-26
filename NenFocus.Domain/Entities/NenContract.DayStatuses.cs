using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Entities;

// What each day owes and got (Gap 95, decisions 5 and 6). Its own file only for size: it is the
// contract's rule (Gap 92, R4 — it was a helper class in Services/).
public partial class NenContract
{
    /// <summary>
    /// Every day of this contract as it stands: what it owes, what was done toward it, the Bonus
    /// hours earned apart from it, and whether it was met — the one day rule the Planner badge,
    /// the banner, the recap and completion all read. <paramref name="blocks"/> are this
    /// contract's own; <paramref name="sessions"/> may be any, the contract decides which count.
    /// Every hour is split across the local days it falls on.
    /// </summary>
    public IReadOnlyList<ContractDayStatus> DayStatuses(
        IEnumerable<Block> blocks, IEnumerable<Session> sessions, Moment asOf, UserTimeZone timeZone)
    {
        var mine = blocks.ToList();
        foreach (var block in mine)
            EnsureMine(block);

        var focusBlocks = mine.OfType<FocusBlock>().ToList();
        var days = _calendar.Days.Select(d => d.Date).ToList();

        // What a day owes: its Committed blocks as planned — missed, ended early or cancelled, a
        // Committed block still counts in full.
        var target = new Dictionary<DateOnly, double>(
            timeZone.HoursPerDay(focusBlocks.Where(b => b.IsCommitted).Select(b => b.Scheduled)));

        // Plus what its redemptions cover: once per missed block per day, never more than that
        // block's own deficit — two attempts at one miss on one day owe it once.
        var blocksById = focusBlocks.ToDictionary(b => b.Id);
        var coverage = focusBlocks
            .Where(b => b.IsRedemption)
            .SelectMany(b => b.Receipt.Entries.Select(e => (Date: timeZone.DateOf(b.Scheduled.Start), e.MissedBlockId, Covering: b)))
            .GroupBy(x => (x.Date, x.MissedBlockId));
        foreach (var claims in coverage)
        {
            var claimed = claims.Aggregate(WholeMinutes.Zero, (sum, x) => sum + x.Covering.Receipt.CoveredFor(x.MissedBlockId));
            var owed = blocksById.TryGetValue(claims.Key.MissedBlockId, out var missed)
                ? WholeMinutes.Min(claimed, missed.OwnDeficit(asOf))
                : claimed;
            target[claims.Key.Date] = target.GetValueOrDefault(claims.Key.Date) + owed.Hours;
        }

        // What was done toward it: the credit of every block that counts toward the floor…
        var worked = focusBlocks
            .Where(b => b.CountsTowardFloor)
            .Select(b => b.CreditedSlot(asOf))
            .OfType<TimeSlot>()
            .ToList();

        // Spontaneous sessions are no longer counted by date here (Gap 114, phase 1): they become
        // walk-in Bonus blocks, whose credit is read off the block. The `sessions` parameter is now
        // unused and is removed with Gap 67's half in phase 2.
        var completed = timeZone.HoursPerDay(worked);

        // Bonus blocks earn hours of their own, never toward the target.
        var bonus = timeZone.HoursPerDay(focusBlocks
            .Where(b => b.IsBonus)
            .Select(b => b.CreditedSlot(asOf))
            .OfType<TimeSlot>());

        return days
            .Select(date => new ContractDayStatus(
                date, target.GetValueOrDefault(date), completed.GetValueOrDefault(date), bonus.GetValueOrDefault(date)))
            .ToList();
    }
}
