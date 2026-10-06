using NenFocus.Application.Interfaces;
using NenFocus.Domain.Services;

namespace NenFocus.Application.Queries.ComputePlannedFocusHours;

public sealed class ComputePlannedFocusHoursQueryHandler
    : IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>>
{
    private readonly IPlannedFocusHoursCalculator _calculator;
    private readonly ISystemClock _clock;

    public ComputePlannedFocusHoursQueryHandler(IPlannedFocusHoursCalculator calculator, ISystemClock clock)
    {
        _calculator = calculator;
        _clock = clock;
    }

    public Task<IReadOnlyDictionary<DateOnly, double>> HandleAsync(
        ComputePlannedFocusHoursQuery query,
        CancellationToken ct = default)
        // The time zone decides "which day" — the user's local day, in real hours (Gap 88).
        => Task.FromResult(_calculator.Calculate(query.DraftFocusBlockRanges, _clock.TimeZone));
}
