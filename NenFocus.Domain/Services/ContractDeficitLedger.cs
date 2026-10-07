using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Services;

/// <summary>
/// The arithmetic behind <see cref="NenContract.Deficits"/> (Gap 106), kept apart as
/// <see cref="ContractDayLedger"/> is, so the contract only says whose blocks.
/// </summary>
internal static class ContractDeficitLedger
{
    /// <summary>
    /// Not a write-off: every whole-minute shortfall is owed (Gap 44 removed). A claim
    /// round-trips through a tick-quantized timestamp and can lose a sub-microsecond sliver;
    /// that residual must never keep Cover Deficit alive forever.
    /// </summary>
    private const double FloatingPointEpsilonMinutes = 1e-6;

    internal static IReadOnlyList<OutstandingDeficit> Build(IReadOnlyList<FocusBlock> focusBlocks, Moment asOf)
    {
        // Only blocks that count toward the floor: a missed Bonus block owes nothing and covers
        // nothing (Gap 97).
        var counted = focusBlocks.Where(b => b.CountsTowardFloor).ToList();
        var redemptions = counted.Where(b => b.Receipt.Entries.Count > 0).ToList();

        var deficits = new List<OutstandingDeficit>();
        foreach (var block in counted)
        {
            var owed = block.OwnDeficit(asOf).TotalMinutes;
            if (owed <= 0) continue;

            var paid = redemptions.Sum(covering => Delivered(covering, block.Id, asOf));
            var remaining = Math.Max(0, owed - paid);
            if (remaining <= FloatingPointEpsilonMinutes) continue;

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
    private static double Delivered(FocusBlock covering, Guid missedBlockId, Moment asOf)
    {
        var claimed = covering.Receipt.MinutesFor(missedBlockId);
        if (claimed <= 0 || !covering.IsFinal(asOf)) return claimed;
        if (covering.Outcome(asOf) == FocusBlockOutcome.Missed) return 0;
        return Math.Min(claimed, covering.CreditedSlot(asOf)?.Length.TotalMinutes ?? 0);
    }
}
