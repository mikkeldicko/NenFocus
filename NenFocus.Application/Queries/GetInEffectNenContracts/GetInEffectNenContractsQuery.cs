using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetInEffectNenContracts;

/// <summary>
/// Every in-effect contract (Active or Scheduled) app-wide, independent of any Planner
/// week — the Active one (at most one ever exists, contract date ranges never overlap)
/// ordered first, then the Scheduled ones by StartDate. Feeds the Nen Contract popover's
/// two sections directly off this one ordering, with no separate resolution logic.
/// </summary>
public sealed record GetInEffectNenContractsQuery : IQuery<IReadOnlyList<NenContractSummaryDto>>;
