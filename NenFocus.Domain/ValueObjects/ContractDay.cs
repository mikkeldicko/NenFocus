namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// One day of a Nen Contract — its local time window and its floor. Every contract day
/// always has a window: Work Hours when configured, otherwise the whole day, with the
/// first day starting at the contract's chosen start and the last day ending at its end.
/// The window is two local instants rather than two TimeOnly values so a window can end
/// at 24:00 (the next day's midnight). A day whose floor could never be met — longer than
/// its own window — cannot be constructed. See OPEN_GAPS.md / DESIGN_GAP_HISTORY.md Gap 65.
/// </summary>
public sealed record ContractDay
{
    public DateOnly Date { get; }
    public DateTime WindowStart { get; }
    public DateTime WindowEnd { get; }

    /// <summary>Null means a rest day — no obligation that day.</summary>
    public DailyFloor? Floor { get; }

    public ContractDay(DateOnly date, DateTime windowStart, DateTime windowEnd, DailyFloor? floor)
    {
        var dayStart = date.ToDateTime(TimeOnly.MinValue);

        if (windowStart.Date != dayStart)
            throw new ArgumentException($"Day {date:yyyy-MM-dd}'s window must start on that day.");

        if (windowEnd <= windowStart)
            throw new ArgumentException($"Day {date:yyyy-MM-dd}'s window must end after it starts.");

        if (windowEnd > dayStart.AddDays(1))
            throw new ArgumentException($"Day {date:yyyy-MM-dd}'s window cannot run past midnight.");

        if (floor is not null && floor.Hours > (windowEnd - windowStart).TotalHours)
            throw new ArgumentException(
                $"Day {date:yyyy-MM-dd}'s floor of {floor.Hours}h is longer than its {(windowEnd - windowStart).TotalHours}h window.");

        Date = date;
        WindowStart = windowStart;
        WindowEnd = windowEnd;
        Floor = floor;
    }

    public bool IsRestDay => Floor is null;

    /// <summary>Whether [localStart, localEnd] fits inside this day's window — ending exactly at WindowEnd is inside.</summary>
    public bool Contains(DateTime localStart, DateTime localEnd)
        => localStart >= WindowStart && localEnd <= WindowEnd;
}
