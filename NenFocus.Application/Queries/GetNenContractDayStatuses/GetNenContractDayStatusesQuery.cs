using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.GetNenContractDayStatuses;

/// <summary>
/// Every day of a Nen Contract as it stands — <c>NenContract.DayStatuses</c>, the one day rule
/// (Gap 95). The Planner badges and banner, the recap and completion all read it. Empty when the
/// contract doesn't exist.
/// </summary>
public sealed record GetNenContractDayStatusesQuery(Guid ContractId) : IQuery<IReadOnlyList<ContractDayStatus>>;
