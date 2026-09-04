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
///
/// For a Period Time Budget contract using ContractFloor (see ActiveNenContractDto),
/// there is no real per-day floor — FloorHours instead carries that day's currently-
/// planned hours (a comparison baseline, not an obligation) and IsMet compares
/// CurrentHours directly against it rather than through IsDayMet, which would trivially
/// always be true (DailyFloorPolicy is None for these contracts). IsRestDay is always
/// false for these contracts — no day is ever "off" when only the period total matters.
/// </summary>
public sealed record NenContractDayStatusDto
{
    public DateOnly Date { get; init; }
    public double CurrentHours { get; init; }
    public double FloorHours { get; init; }
    public bool IsMet { get; init; }
    public bool IsRestDay { get; init; }
    public DayTiming Timing { get; init; }

    /// <summary>
    /// Completed hours from Bonus blocks (ContractId null) on this day — never counted
    /// toward FloorHours/CurrentHours/IsMet above; a genuinely separate number, shown
    /// alongside the floor badge, not folded into it.
    /// </summary>
    public double BonusHours { get; init; }
}
