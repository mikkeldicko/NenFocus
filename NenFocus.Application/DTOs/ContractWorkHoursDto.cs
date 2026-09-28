namespace NenFocus.Application.DTOs;

/// <summary>
/// A contract's window on one specific date, as local instants — NenContract.WindowOn,
/// the same per-day window NenContract.AllowsBlockPlacement checks against. Instants rather
/// than times of day so a window can end at 24:00 (the next day's midnight). A null result
/// (see GetContractWorkHoursForDateQuery) means the date is outside the contract.
/// </summary>
public sealed record ContractWorkHoursDto(DateTime Start, DateTime End);
