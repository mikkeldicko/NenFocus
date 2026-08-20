using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.FindMakeUpEligibleBlock;

/// <summary>
/// Given a contract, a preset, and a proposed date, finds the earliest still-incomplete
/// Focus Block under that contract sharing the same preset whose deficit the proposed
/// date is eligible to cover (NenContract.CheckMakeUpEligibility) — or null if none
/// qualifies. Feeds the Planner form's "mark as makeup" affordance: composes the existing
/// deficit ledger rather than re-deriving it, and defers eligibility entirely to the
/// domain method already used by CheckMakeUpForEntryQuery, never re-implementing it here.
/// </summary>
public sealed record FindMakeUpEligibleBlockQuery(
    Guid ContractId,
    Guid PresetId,
    DateTime ProposedDate) : IQuery<FocusBlockDeficitDto?>;
