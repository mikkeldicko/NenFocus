using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.FindRedemptionEligibleBlock;

/// <summary>
/// Given a contract, a preset, and a proposed date, finds the earliest still-incomplete
/// Focus Block under that contract sharing the same preset whose deficit the proposed
/// date is eligible to cover (NenContract.CheckRedemptionEligibility) — or null if none
/// qualifies. Feeds the Planner form's "mark as redemption" affordance: composes the existing
/// deficit ledger rather than re-deriving it, and defers eligibility entirely to the
/// domain method already used by CheckRedemptionEntryQuery, never re-implementing it here.
/// </summary>
public sealed record FindRedemptionEligibleBlockQuery(
    Guid ContractId,
    Guid PresetId,
    DateTime ProposedDate) : IQuery<FocusBlockDeficitDto?>;
