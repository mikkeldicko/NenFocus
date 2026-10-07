using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetNenContractDeficits;

/// <summary>
/// Returns every missed or late Focus Block under the contract that still carries
/// uncovered deficit — feeds the Planner's "Redemption" checklist. Only accounts for
/// deficit covered by redemption blocks' receipts; spontaneous redemption sessions don't yet
/// write back to this ledger — see the Spontaneous redemption path (Gap 72).
/// The contract works it out (<c>NenContract.Deficits</c>, Gap 106).
/// </summary>
public sealed record GetNenContractDeficitsQuery(Guid ContractId)
    : IQuery<IReadOnlyList<FocusBlockDeficitDto>>;
