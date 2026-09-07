using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetNenContractCompletionReadiness;

/// <summary>
/// Whether the given contract is ready to be completed early via the Planner's "Complete
/// Contract" button — see NenContractCompletionReadinessDto. Not found or not Active both
/// resolve to IsReady = false rather than throwing, since the Planner button binds to this
/// query directly to decide its own visibility.
/// </summary>
public sealed record GetNenContractCompletionReadinessQuery(Guid ContractId) : IQuery<NenContractCompletionReadinessDto>;
