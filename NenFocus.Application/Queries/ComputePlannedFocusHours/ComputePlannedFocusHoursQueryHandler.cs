using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.ComputePlannedFocusHours;

public sealed class ComputePlannedFocusHoursQueryHandler
    : IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IPlannedFocusHoursCalculator _calculator;
    private readonly ISystemClock _clock;

    public ComputePlannedFocusHoursQueryHandler(IPlannedBlockRepository blocks, IPlannedFocusHoursCalculator calculator, ISystemClock clock)
    {
        _blocks = blocks;
        _calculator = calculator;
        _clock = clock;
    }

    public async Task<IReadOnlyDictionary<DateOnly, double>> HandleAsync(
        ComputePlannedFocusHoursQuery query,
        CancellationToken ct = default)
    {
        var focusTime = new List<TimeSlot>();

        if (query.ContractId.HasValue)
        {
            var persisted = await _blocks.GetForContractAsync(query.ContractId.Value, ct);
            focusTime.AddRange(persisted
                .OfType<FocusBlock>()
                .Where(b => b.Id != query.ExcludeBlockId)
                .Select(b => b.Scheduled));
        }

        focusTime.AddRange(query.DraftFocusBlockRanges);

        // The time zone decides "which day" — the user's local day, in real hours (Gap 88).
        return _calculator.Calculate(focusTime, _clock.TimeZone);
    }
}
