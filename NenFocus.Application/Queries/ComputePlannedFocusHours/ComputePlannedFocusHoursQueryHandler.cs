using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.ComputePlannedFocusHours;

public sealed class ComputePlannedFocusHoursQueryHandler
    : IQueryHandler<ComputePlannedFocusHoursQuery, IReadOnlyDictionary<DateOnly, double>>
{
    private readonly ISystemClock _clock;

    public ComputePlannedFocusHoursQueryHandler(ISystemClock clock)
    {
        _clock = clock;
    }

    public Task<IReadOnlyDictionary<DateOnly, double>> HandleAsync(
        ComputePlannedFocusHoursQuery query,
        CancellationToken ct = default)
        // The time zone decides "which day" — the user's local day, in real hours (Gap 88).
        => Task.FromResult(_clock.TimeZone.HoursPerDay(query.DraftFocusBlockRanges));
}
