using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetContractWorkHoursForDate;

/// <summary>
/// The window <paramref name="ContractId"/> enforces on <paramref name="Date"/> (its Work
/// Hours, or the whole day — trimmed to the contract's start/end on its first/last day), or
/// null when the date is outside the contract. Exists so a caller that needs to pre-check or
/// display the window before a command round-trip — the Planner's Split dialog's live
/// validation — can reuse the real per-day resolution instead of re-deriving it.
/// </summary>
public sealed record GetContractWorkHoursForDateQuery(Guid ContractId, DateOnly Date) : IQuery<ContractWorkHoursDto?>;
