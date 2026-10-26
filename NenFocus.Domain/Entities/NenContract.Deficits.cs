using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Entities;

// What this contract's missed and late blocks still owe (Gap 106). Its own file only for size: it
// is the contract's rule (Gap 92, R4 — it was a helper class in Services/).
public partial class NenContract
{
    /// <summary>
    /// Every missed or late block of this contract still owed, oldest first — what it failed to
    /// deliver, and what is left once redemptions have paid (Gap 106). <paramref name="blocks"/>
    /// are this contract's own. Whole minutes throughout, so a debt paid exactly is exactly zero —
    /// no tolerance (Gap 92, V10; it replaced a 1e-6 guard against Gap 33's floating-point slivers).
    /// </summary>
    public IReadOnlyList<OutstandingDeficit> Deficits(IEnumerable<Block> blocks, Moment asOf)
    {
        var mine = blocks.ToList();
        foreach (var block in mine)
            EnsureMine(block);

        // Only blocks that count toward the floor: a missed Bonus block owes nothing and covers
        // nothing (Gap 97).
        var counted = mine.OfType<FocusBlock>().Where(b => b.CountsTowardFloor).ToList();
        var redemptions = counted.Where(b => b.Receipt.Entries.Count > 0).ToList();

        var deficits = new List<OutstandingDeficit>();
        foreach (var block in counted)
        {
            var owed = block.OwnDeficit(asOf);
            if (owed == WholeMinutes.Zero) continue;

            var paid = redemptions.Aggregate(WholeMinutes.Zero, (sum, covering) => sum + Delivered(covering, block.Id, asOf));
            var remaining = owed.Less(paid);
            if (remaining == WholeMinutes.Zero) continue;

            // A block with a deficit of its own is final with a shortfall outcome.
            deficits.Add(new OutstandingDeficit(
                block.Id, block.BlockPresetId, block.Scheduled, block.Outcome(asOf)!.Value, owed, remaining));
        }

        return deficits.OrderBy(d => d.Scheduled).ToList();
    }

    /// <summary>
    /// Rule 13's second half: a claim counts only for what its block delivered. Not final yet
    /// (Rule 18) — trusted in full; missed — nothing; otherwise at most its own credit.
    /// </summary>
    private static WholeMinutes Delivered(FocusBlock covering, Guid missedBlockId, Moment asOf)
    {
        var claimed = covering.Receipt.CoveredFor(missedBlockId);
        if (claimed == WholeMinutes.Zero || !covering.IsFinal(asOf)) return claimed;
        if (covering.Outcome(asOf) == FocusBlockOutcome.Missed) return WholeMinutes.Zero;
        return WholeMinutes.Min(claimed, covering.CreditAt(asOf));
    }
}
