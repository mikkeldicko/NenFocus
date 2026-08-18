using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

/// <summary>
/// One day's daily-floor status within a sealed contract's full [StartDate, EndDate]
/// range — feeds the Planner's per-day floor badges. CurrentHours is completed Focus
/// hours when Timing is Today or Past, otherwise planned Focus hours from
/// already-scheduled blocks; IsMet is computed against whichever of those applies, via
/// NenContract.IsDayMet, so the same day-met rule covers both cases uniformly. Timing
/// gives "today, not yet met" its own distinct treatment from "past, missed" — the day
/// isn't over yet, so it isn't a real miss — without allowing the impossible
/// Today-and-Past combination two loose bools would.
/// </summary>
public sealed record NenContractDayStatusDto
{
    public DateOnly Date { get; init; }
    public double CurrentHours { get; init; }
    public double FloorHours { get; init; }
    public bool IsMet { get; init; }
    public bool IsRestDay { get; init; }
    public DayTiming Timing { get; init; }
}
