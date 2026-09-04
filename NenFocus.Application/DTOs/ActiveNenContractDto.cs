using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

/// <summary>
/// Feeds the Planner's persistent active-contract banner:
/// "Contract: [Name] — Day X/Y — Hh / Th completed — Dh deficit".
/// Also feeds the Nen Contract popover's Active section.
/// </summary>
public sealed record ActiveNenContractDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public StrictnessRule StrictnessRule { get; init; }

    /// <summary>Drives how tightly the Planner's Cover Deficit form bounds its date field.</summary>
    public RedemptionPlacementPolicy RedemptionPlacementPolicy { get; init; }

    /// <summary>1-based day of the contract "today" falls on — clamped into [1, TotalDays].</summary>
    public int DayNumber { get; init; }
    public int TotalDays { get; init; }

    /// <summary>Non-rest-day count from StartDate through today (or EndDate) with IsDayMet false.
    /// Zero for a ContractFloor contract (see below) — Day-Met has no meaning there. No longer
    /// shown in the banner text, but kept for any future per-day-scoring consumer (e.g. streaks).</summary>
    public int DaysBehind { get; init; }

    /// <summary>Non-rest-day count from StartDate through today (or EndDate) — DaysBehind's denominator.</summary>
    public int DaysConsidered { get; init; }

    public double DeficitHours { get; init; }

    /// <summary>Mirrors NenContract.AllowCancellation — drives whether the Planner's Cancel button is shown.</summary>
    public bool AllowCancellation { get; init; }

    /// <summary>
    /// Non-null only for a Period Time Budget contract using a whole-period ContractFloor
    /// instead of a per-day floor — the total Focus hours committed at Seal. Also drives
    /// DayStatuses' per-day badge behavior (no rest days, Past shows a single completed-hours
    /// number instead of "completed/planned"). TotalFloorHours/TotalCompletedHours below
    /// already fold this contract's numbers in for the banner — this field exists purely as
    /// the mode discriminator for badge rendering, not a second source of the same total.
    /// </summary>
    public double? ContractFloorHours { get; init; }

    /// <summary>
    /// The banner's denominator for every contract, not just ContractFloor ones: that
    /// contract's ContractFloorHours when set, otherwise the sum of every day's resolved
    /// floor across [StartDate, EndDate] (rest days contribute 0) — the total minimum
    /// commitment across the whole contract, computed the same way regardless of strictness.
    /// </summary>
    public double TotalFloorHours { get; init; }

    /// <summary>Total completed Focus hours across the whole contract so far — the banner's
    /// numerator, paired with TotalFloorHours. Same value for every contract regardless of
    /// strictness or ContractFloor mode — a straight sum of completed hours per day.</summary>
    public double TotalCompletedHours { get; init; }

    /// <summary>
    /// One entry per day across the contract's full [StartDate, EndDate] range (unlike
    /// DaysBehind/DaysConsidered, which only ever look at StartDate..today) — feeds the
    /// Planner's per-day floor badges, including future/planned days.
    /// </summary>
    public IReadOnlyList<NenContractDayStatusDto> DayStatuses { get; init; } = [];
}
