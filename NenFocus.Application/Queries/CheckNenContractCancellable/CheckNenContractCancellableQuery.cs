using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.CheckNenContractCancellable;

/// <summary>
/// May this contract be cancelled (broken) right now — <c>NenContract.CheckBreak</c>'s answer, the
/// same one the Break validator and handler ask, so the Planner's Cancel button and the command
/// can't disagree. Null when the contract doesn't exist or is no longer in effect.
/// </summary>
public sealed record CheckNenContractCancellableQuery(Guid ContractId) : IQuery<ContractEndVerdictDto?>;
