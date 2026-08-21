using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;

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
        var now = _clock.UtcNow;
        var focusBlocks = (await _blocks.GetForContractAsync(query.ContractId, ct))
            .OfType<FocusBlock>()
            .ToList();

        var deficits = new List<FocusBlockDeficitDto>();

        foreach (var block in focusBlocks)
        {
            var session = await _sessions.GetByOriginatingBlockIdAsync(block.Id, ct);
            var outcome = _classifier.Classify(block.Start, block.End, block.RitualCompletedAt, session?.EndedAt, now);
            if (!block.CanContributeOwnDeficit(outcome))
                continue;

            // Credited, not raw elapsed: a StartedLate session that keeps running past the
            // block's own scheduled end (e.g. a fixed-duration session started late) can
            // rack up more raw elapsed minutes than the block was ever scheduled for. Raw
            // elapsed would then make rawDeficit collapse to zero — hiding a real deficit —
            // purely because of time spent *after* the block's window closed. Credited
            // minutes is already the correct, capped answer (same rule Daily Floor scoring
            // uses via GetNenContractCompletedFocusHoursQueryHandler), reused here rather
            // than re-derived, so the two can never disagree about "how much of this block
            // was actually honored."
            var actualElapsedMinutes = session?.GetElapsedTime(now)?.TotalMinutes ?? 0;
            var creditedMinutes = _classifier.CreditedFocusMinutes(
                block.Start, block.End, block.RitualCompletedAt, session?.EndedAt, actualElapsedMinutes);
            var scheduledMinutes = block.GetDuration().TotalMinutes;
            var rawDeficit = Math.Max(0, scheduledMinutes - creditedMinutes);

            var coveredMinutes = focusBlocks
                .Where(b => b.Id != query.ExcludeCoverageFromBlockId)
                .SelectMany(b => b.RedemptionEntries)
                .Where(e => e.MissedBlockId == block.Id)
                .Sum(e => e.MinutesCovered);

            var remainingDeficit = Math.Max(0, rawDeficit - coveredMinutes);
            if (remainingDeficit <= 0)
                continue;

            deficits.Add(new FocusBlockDeficitDto
            {
                FocusBlockId = block.Id,
                BlockPresetId = block.BlockPresetId,
                ScheduledStart = block.Start,
                ScheduledEnd = block.End,
                // CanContributeOwnDeficit already guarantees outcome is one of the three
                // shortfall values, not null — the compiler just can't see that through the
                // method call.
                Outcome = outcome!.Value,
                RemainingDeficitMinutes = remainingDeficit,
                PlannedMinutes = scheduledMinutes,
                CompletedMinutes = creditedMinutes
            });
        }

        return deficits.OrderBy(d => d.ScheduledStart).ToList();
    }
}
