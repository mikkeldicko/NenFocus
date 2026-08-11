namespace NenFocus.Application.DTOs;

/// <summary>
/// Feeds the Planner's persistent active-contract banner:
/// "Contract: [Name] — Day X/Y — Z of N days behind — Hh deficit".
/// </summary>
public sealed record ActiveNenContractDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }

    /// <summary>1-based day of the contract "today" falls on — clamped into [1, TotalDays].</summary>
    public int DayNumber { get; init; }
    public int TotalDays { get; init; }

    /// <summary>Non-rest-day count from StartDate through today (or EndDate) with IsDayMet false.</summary>
    public int DaysBehind { get; init; }

    /// <summary>Non-rest-day count from StartDate through today (or EndDate) — DaysBehind's denominator.</summary>
    public int DaysConsidered { get; init; }

    public double DeficitHours { get; init; }

    /// <summary>Mirrors NenContract.AllowCancellation — drives whether the Planner's Cancel button is shown.</summary>
    public bool AllowCancellation { get; init; }
}
