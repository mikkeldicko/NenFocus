using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetActiveNenContract;

/// <summary>
/// The contract to surface on the Planner's active-contract banner for the currently
/// viewed week (WeekStart–WeekEnd, both inclusive), or null when none applies. Among
/// in-effect (Scheduled or Active) contracts whose date range overlaps the viewed week,
/// an Active one is preferred over a merely-Scheduled one, then the earliest StartDate —
/// only one contract is ever surfaced at a time. A contract's Day-X/Y and deficit figures
/// are still computed relative to "today" (the clock), independent of which week is viewed.
/// </summary>
public sealed record GetActiveNenContractQuery(DateOnly WeekStart, DateOnly WeekEnd) : IQuery<ActiveNenContractDto?>;
