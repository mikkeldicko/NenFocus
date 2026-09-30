using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;
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

        var now = _clock.Now;

        // Coarse pre-filter (a day either side, so no boundary block is missed) — the
        // precise "is this block's Start actually within the contract's range" check
        // below is what actually matters, same rule GetPlannedBlocksForDayQueryHandler's
        // tile-level Bonus resolution already uses.
        var rangeStart = contract.StartDate.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        var rangeEnd = contract.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var blocksInRange = await _blocks.GetForRangeAsync(
            new TimeSlot(_clock.TimeZone.ToMoment(rangeStart), _clock.TimeZone.ToMoment(rangeEnd)), ct);

        // StartsAt/EndsAt, not a date-only truncation of Start — a Work-Hours contract's
        // true boundary isn't midnight, so a block placed after Work Hours close on the
        // last day (or before they open on the first) must not still count as Bonus just
        // because it shares the calendar day. b.Start is UTC; StartsAt/EndsAt are local
        // wall-clock instants, so the comparison must happen in local time.
        var bonusBlocks = blocksInRange
            .OfType<FocusBlock>()
            .Where(b => b.ContractId is null)
            .Where(b => contract.Covers(_clock.TimeZone.ToCalendarTimes(b.Scheduled).Start))
            .ToList();

        var creditedWindows = new List<TimeSlot>();
        foreach (var block in bonusBlocks)
        {
            var session = await _sessions.GetByOriginatingBlockIdAsync(block.Id, ct);
            var actualElapsedMinutes = session?.GetElapsedTime(now)?.TotalMinutes ?? 0;
            var window = _classifier.CreditedWindow(
                block.Scheduled, block.RitualCompletedAt, session?.EndedAt, actualElapsedMinutes);

            if (window is { } w)
                creditedWindows.Add(w);
        }

        return _calculator.Calculate(creditedWindows, _clock.TimeZone);
    }
}
