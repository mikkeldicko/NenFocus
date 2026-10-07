using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

/// <summary>
/// One day of a sealed contract as the Planner's day badge shows it — <c>ContractDayStatus</c>
/// (<c>NenContract.DayStatuses</c>, the one day rule, Gap 95), mapped for display. TargetHours is
/// what the day owes. CurrentHours is what was done when Timing is Today or Past; on a future day
/// it is what the day owes as planned, met as planned. Timing gives "today, not yet met" its own
/// treatment apart from "past, missed" — the day isn't over yet, so it isn't a real miss.
/// </summary>
public sealed record NenContractDayStatusDto
{
    public DateOnly Date { get; init; }
    public double CurrentHours { get; init; }
    public double TargetHours { get; init; }
    public bool IsMet { get; init; }
    public bool IsRestDay { get; init; }
    public DayTiming Timing { get; init; }

    /// <summary>
    /// Completed hours from the contract's Bonus blocks on this day — never counted
    /// toward TargetHours/CurrentHours/IsMet above; a genuinely separate number, shown
    /// alongside the floor badge, not folded into it.
    /// </summary>
    public double BonusHours { get; init; }
}
