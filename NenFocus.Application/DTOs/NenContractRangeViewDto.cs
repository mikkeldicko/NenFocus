using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

/// <summary>
/// One contract's view within a caller-chosen date range — feeds the Planner's
/// contract banner and per-day floor badges. Renamed from ActiveNenContractDto: a single
/// visible range can now surface more than one in-effect contract (one ending, the next
/// starting, mid-range) and the Planner lets the user switch which one the banner/badges
/// currently reflect, so "the active one" is no longer an accurate name for what an
/// individual entry holds — Status makes that explicit per entry instead.
/// </summary>
public sealed record NenContractRangeViewDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public StrictnessRule StrictnessRule { get; init; }

    /// <summary>
    /// Scheduled or Active — GetNenContractsForDateRangeQuery only ever returns in-effect
    /// contracts. Drives the Planner's "Upcoming" banner indicator directly (Scheduled),
    /// with no separate derived flag needed.
    /// </summary>
    public NenContractStatus Status { get; init; }

    /// <summary>Drives how tightly the Planner's Cover Deficit form bounds its date field.</summary>
    public RedemptionPlacementPolicy RedemptionPlacementPolicy { get; init; }

    /// <summary>1-based day of the contract "today" falls on — clamped into [1, TotalDays].</summary>
    public int DayNumber { get; init; }
    public int TotalDays { get; init; }

    /// <summary>Non-rest-day count from StartDate through today (or EndDate) that are not met (ContractDayStatus.IsMet).
    /// Zero for a period-target contract (see HasPeriodTarget) — Day-Met has no meaning there. No longer
    /// shown in the banner text, but kept for any future per-day-scoring consumer (e.g. streaks).</summary>
    public int DaysBehind { get; init; }

    /// <summary>Non-rest-day count from StartDate through today (or EndDate) — DaysBehind's denominator.</summary>
    public int DaysConsidered { get; init; }

    public double DeficitHours { get; init; }

    /// <summary>Mirrors NenContract.AllowCancellation — drives whether the Planner's Cancel button is shown.</summary>
    public bool AllowCancellation { get; init; }

    /// <summary>
    /// True for a Period Time Budget contract held to a whole-period target instead of per-day
    /// floors (<c>NenContract.HasPeriodTarget</c>, Gap 95). Drives DayStatuses' per-day badge behavior
    /// (no rest days, Past shows a single completed-hours number). The target itself is
    /// TotalFloorHours below — this is only the mode.
    /// </summary>
    public bool HasPeriodTarget { get; init; }

    /// <summary>
    /// The banner's denominator for every contract: CommittedFocusHours, what was planned at
    /// Seal — computed the same way regardless of strictness.
    /// </summary>
    public double TotalFloorHours { get; init; }

    /// <summary>Total completed Focus hours across the whole contract so far — the banner's
    /// numerator, paired with TotalFloorHours. Same value for every contract regardless of
    /// strictness or period-target mode — a straight sum of completed hours per day.</summary>
    public double TotalCompletedHours { get; init; }

    /// <summary>Completed hours from Bonus blocks across the whole contract — never part of
    /// TotalFloorHours/TotalCompletedHours above, shown as its own separate banner stat.</summary>
    public double TotalBonusHours { get; init; }

    /// <summary>
    /// One entry per day across the contract's full [StartDate, EndDate] range (unlike
    /// DaysBehind/DaysConsidered, which only ever look at StartDate..today) — feeds the
    /// Planner's per-day floor badges, including future/planned days.
    /// </summary>
    public IReadOnlyList<NenContractDayStatusDto> DayStatuses { get; init; } = [];
}
