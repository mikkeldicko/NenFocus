using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetContractWorkHoursForDate;

/// <summary>
/// The work-hours window <paramref name="ContractId"/> enforces on <paramref name="Date"/>,
/// or null when there's no constraint that day (no WorkHoursPolicy at all, or an explicit
/// no-constraint exception for that date). Exists so a caller that needs to pre-check or
/// display the window before a command round-trip — the Planner's Split dialog's live
/// validation — can reuse the real per-day resolution instead of re-deriving it.
/// </summary>
public sealed record GetContractWorkHoursForDateQuery(Guid ContractId, DateOnly Date) : IQuery<ContractWorkHoursDto?>;
