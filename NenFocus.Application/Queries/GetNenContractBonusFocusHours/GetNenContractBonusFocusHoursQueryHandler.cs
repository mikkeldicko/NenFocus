using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Services;

namespace NenFocus.Application.Queries.GetNenContractBonusFocusHours;

public sealed class GetNenContractBonusFocusHoursQueryHandler
    : IQueryHandler<GetNenContractBonusFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>>
{
    private readonly INenContractRepository _contracts;
    private readonly IPlannedBlockRepository _blocks;
    private readonly ISessionRepository _sessions;
    private readonly IFocusBlockOutcomeClassifier _classifier;
    private readonly IPlannedFocusHoursCalculator _calculator;
    private readonly ISystemClock _clock;

    public GetNenContractBonusFocusHoursQueryHandler(
        INenContractRepository contracts,
        IPlannedBlockRepository blocks,
        ISessionRepository sessions,
        IFocusBlockOutcomeClassifier classifier,
        IPlannedFocusHoursCalculator calculator,
        ISystemClock clock)
    {
        _contracts = contracts;
        _blocks = blocks;
        _sessions = sessions;
        _classifier = classifier;
        _calculator = calculator;
        _clock = clock;
    }

    public async Task<IReadOnlyDictionary<DateOnly, double>> HandleAsync(
        GetNenContractBonusFocusHoursQuery query,
        CancellationToken ct = default)
    {
        var contract = await _contracts.GetByIdAsync(query.ContractId, ct);
        if (contract is null)
            return new Dictionary<DateOnly, double>();

        var now = _clock.UtcNow;

        // Coarse pre-filter (a day either side, so no boundary block is missed) — the
        // precise "is this block's Start actually within the contract's range" check
        // below is what actually matters, same rule GetPlannedBlocksForDayQueryHandler's
        // tile-level Bonus resolution already uses.
        var rangeStart = contract.StartDate.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        var rangeEnd = contract.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var blocksInRange = await _blocks.GetForRangeAsync(rangeStart, rangeEnd, ct);

        var bonusBlocks = blocksInRange
            .OfType<FocusBlock>()
            .Where(b => b.ContractId is null)
            .Where(b => DateOnly.FromDateTime(b.Start) >= contract.StartDate && DateOnly.FromDateTime(b.Start) <= contract.EndDate)
            .ToList();

        var creditedWindows = new List<(DateTime Start, DateTime End)>();
        foreach (var block in bonusBlocks)
        {
            var session = await _sessions.GetByOriginatingBlockIdAsync(block.Id, ct);
            var actualElapsedMinutes = session?.GetElapsedTime(now)?.TotalMinutes ?? 0;
            var window = _classifier.CreditedWindow(
                block.Start, block.End, block.RitualCompletedAt, session?.EndedAt, actualElapsedMinutes);

            if (window is { } w)
                creditedWindows.Add((_clock.ToLocal(w.Start), _clock.ToLocal(w.End)));
        }

        return _calculator.Calculate(creditedWindows);
    }
}
