using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.GetNenContractDeficits;

public sealed class GetNenContractDeficitsQueryHandler
    : IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly ISessionRepository _sessions;
    private readonly IFocusBlockOutcomeClassifier _classifier;
    private readonly ISystemClock _clock;

    public GetNenContractDeficitsQueryHandler(
        IPlannedBlockRepository blocks,
        ISessionRepository sessions,
        IFocusBlockOutcomeClassifier classifier,
        ISystemClock clock)
    {
        _blocks = blocks;
        _sessions = sessions;
        _classifier = classifier;
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

        // Every block's own outcome + credited minutes, resolved once up front — not just
        // for blocks with a deficit of their own, but for every block, since a covering
        // block's own real outcome is what Rule 13's second half needs to judge whether its
        // RedemptionEntries actually mean anything (see the coveredMinutes loop below).
        var resolved = new Dictionary<Guid, (FocusBlockOutcome? Outcome, double CreditedMinutes)>();
        foreach (var block in focusBlocks)
        {
            var session = await _sessions.GetByOriginatingBlockIdAsync(block.Id, ct);
            var outcome = _classifier.Classify(block, now);
            var actualElapsedMinutes = session?.GetElapsedTime(now)?.TotalMinutes ?? 0;
            // Credited, not raw elapsed: a StartedLate session that keeps running past the
            // block's own scheduled end (e.g. a fixed-duration session started late) can
            // rack up more raw elapsed minutes than the block was ever scheduled for. Raw
            // elapsed would then make rawDeficit collapse to zero — hiding a real deficit —
            // purely because of time spent *after* the block's window closed. Credited
            // minutes is already the correct, capped answer (same rule Daily Floor scoring
            // uses via GetNenContractCompletedFocusHoursQueryHandler), reused here rather
            // than re-derived, so the two can never disagree about "how much of this block
            // was actually honored."
            var creditedMinutes = _classifier.CreditedFocusMinutes(
            block.Scheduled, block.RitualCompletedAt, session?.EndedAt, actualElapsedMinutes);

            // Classify answers "what does this look like right now" — a deliberately live
            // read other consumers (Day Met's progress number, Insights, Planner badges) rely
            // on updating in real time. The deficit ledger asks a narrower, second question:
            // has this outcome actually settled yet. Until the session has genuinely ended
            // (naturally or early), treat the outcome as still-open (null) *for this handler
            // only* — every consumer below already has correct handling for null (excluded as
            // a deficit source; trusted optimistically as a covering block, since it hasn't
            // failed yet). A StartedLate block whose session is still running is the case this
            // exists for: its final credited minutes aren't knowable yet (it could still end
            // early), so nothing downstream should be allowed to treat today's live number as
            // final.
            var hasEnded = session?.HasEnded ?? false;
            var effectiveOutcome = hasEnded ? outcome : null;
            resolved[block.Id] = (effectiveOutcome, creditedMinutes);
        }

        var deficits = new List<FocusBlockDeficitDto>();

        foreach (var block in focusBlocks)
        {
            var (outcome, creditedMinutes) = resolved[block.Id];
            if (!block.CanContributeOwnDeficit(outcome))
                continue;

            var scheduledMinutes = block.GetDuration().TotalMinutes;
            var rawDeficit = block.RawDeficitMinutes(creditedMinutes);

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
                // CanContributeOwnDeficit already guarantees outcome is one of the three
                // shortfall values, not null — the compiler just can't see that through the
                // method call.
                Outcome = outcome!.Value,
                RemainingDeficitMinutes = remainingDeficit,
                PlannedMinutes = scheduledMinutes,
                CompletedMinutes = creditedMinutes
            });
        }

        return deficits.OrderBy(d => d.Scheduled).ToList();
    }
}
