namespace NenFocus.Application.DTOs;

/// <summary>
/// The work-hours window a contract enforces for one specific date, resolved via
/// WorkHoursPolicy.For(date) — the exact same per-day resolution
/// NenContract.AllowsBlockPlacement uses internally. A null result (see
/// GetContractWorkHoursForDateQuery) means no constraint that day.
/// </summary>
public sealed record ContractWorkHoursDto(TimeOnly Start, TimeOnly End);
