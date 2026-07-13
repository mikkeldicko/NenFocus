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
            var outcome = _classifier.Classify(block.Start, block.End, block.RitualCompletedAt, now);
            if (outcome is not (FocusBlockOutcome.Missed or FocusBlockOutcome.StartedLate))
                continue;

            var session = await _sessions.GetByOriginatingBlockIdAsync(block.Id, ct);
            var actualElapsedMinutes = session?.GetElapsedTime(now)?.TotalMinutes ?? 0;
            var scheduledMinutes = block.GetDuration().TotalMinutes;
            var rawDeficit = Math.Max(0, scheduledMinutes - actualElapsedMinutes);

            var coveredMinutes = focusBlocks
                .SelectMany(b => b.MakeUpForEntries)
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
                Outcome = outcome.Value,
                RemainingDeficitMinutes = remainingDeficit
            });
        }

        return deficits.OrderBy(d => d.ScheduledStart).ToList();
    }
}
