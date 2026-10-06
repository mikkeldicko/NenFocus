using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.GetNenContractDeficits;

public sealed class GetNenContractDeficitsQueryHandler
    : IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly ISystemClock _clock;

    public GetNenContractDeficitsQueryHandler(IPlannedBlockRepository blocks, ISystemClock clock)
    {
        _blocks = blocks;
        _clock = clock;
    }

    public async Task<IReadOnlyList<FocusBlockDeficitDto>> HandleAsync(
        GetNenContractDeficitsQuery query,
        CancellationToken ct = default)
    {
        var now = _clock.Now;
        // Only the blocks that count toward the floor: a missed Bonus block owes nothing and
        // covers nothing (Gap 97).
        var focusBlocks = (await _blocks.GetForContractAsync(query.ContractId, ct))
            .OfType<FocusBlock>()
            .Where(b => b.CountsTowardFloor)
            .ToList();

        // Every block answers from its own stamps (Gap 95). A covering block's outcome is only
        // trusted once it is final (Rule 18): a session still running could still end early, so
        // until then it counts as still open.
        var resolved = focusBlocks.ToDictionary(
            b => b.Id,
            b => (Outcome: b.IsFinal(now) ? b.Outcome(now) : null,
                  CreditedMinutes: b.CreditedSlot(now)?.Length.TotalMinutes ?? 0));

        var deficits = new List<FocusBlockDeficitDto>();

        foreach (var block in focusBlocks)
        {
            var rawDeficit = block.OwnDeficit(now).TotalMinutes;
            if (rawDeficit <= 0)
                continue;

            var (outcome, creditedMinutes) = resolved[block.Id];

            // Rule 13's second half: a covering block's RedemptionEntry only counts for as
            // much as that block actually delivered. Still-open (null outcome, scheduled
            // ahead but not yet judged) trusts the planned claim — a redemption isn't
            // penalized just for not having happened yet. Missed contributes nothing — it
            // never delivered anything, so the original deficit it claimed to cover reopens.
            // Anything else (CompletedOnTime/StartedLate/EndedEarly) is capped at that
            // block's own real credited minutes, never the frozen creation-time estimate.
            var coveredMinutes = focusBlocks
                .Where(b => b.Id != query.ExcludeCoverageFromBlockId)
                .SelectMany(b => b.RedemptionEntries.Select(entry => (Covering: b, Entry: entry)))
                .Where(x => x.Entry.MissedBlockId == block.Id)
                .Sum(x =>
                {
                    var (coveringOutcome, coveringCreditedMinutes) = resolved[x.Covering.Id];
                    return coveringOutcome switch
                    {
                        null => x.Entry.MinutesCovered,
                        FocusBlockOutcome.Missed => 0,
                        _ => Math.Min(x.Entry.MinutesCovered, coveringCreditedMinutes)
                    };
                });

            // No product-level write-off any more (Gap 44 removed) — every whole-minute
            // shortfall is a real, visible deficit, recoverable through Cover Deficit down
            // to the last minute. What's left is a pure floating-point safety guard, several
            // orders of magnitude below a minute: MinutesCovered round-trips through a
            // DateTime's tick quantization, which can leave a microscopic (sub-microsecond)
            // residual behind for a redemption that visibly, functionally fully covers its
            // target. That residual isn't a real deficit and must never keep Cover Deficit
            // alive forever — a completely different concern from the removed 1-minute
            // product forgiveness, so it gets a completely different-sized threshold.
            const double floatingPointEpsilonMinutes = 1e-6;
            var remainingDeficit = Math.Max(0, rawDeficit - coveredMinutes);
            if (remainingDeficit <= floatingPointEpsilonMinutes)
                continue;

            deficits.Add(new FocusBlockDeficitDto
            {
                FocusBlockId = block.Id,
                BlockPresetId = block.BlockPresetId,
                Scheduled = block.Scheduled,
                // A block with a deficit of its own is final with a shortfall outcome.
                Outcome = outcome!.Value,
                RemainingDeficitMinutes = remainingDeficit,
                PlannedMinutes = block.GetDuration().TotalMinutes,
                CompletedMinutes = creditedMinutes
            });
        }

        return deficits.OrderBy(d => d.Scheduled).ToList();
    }
}
