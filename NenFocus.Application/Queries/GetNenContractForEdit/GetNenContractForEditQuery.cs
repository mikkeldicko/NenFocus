using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetNenContractForEdit;

/// <summary>
/// Bundles a Scheduled contract's rules and already-drafted blocks into the one payload the
/// Contract Wizard needs to reopen pre-filled. Returns null if the contract doesn't exist.
/// Does not itself enforce that the contract is Scheduled — callers (the popover's Edit
/// action) only ever offer this for contracts already known to be Scheduled.
/// </summary>
public sealed record GetNenContractForEditQuery(Guid ContractId) : IQuery<NenContractEditDto?>;
