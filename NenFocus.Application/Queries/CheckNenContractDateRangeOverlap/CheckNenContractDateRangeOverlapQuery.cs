using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.CheckNenContractDateRangeOverlap;

/// <summary>
/// Checks a proposed contract date range against every in-effect (Scheduled or Active)
/// contract. StartDate/EndDate are inclusive on both ends, so two contracts sharing even
/// one boundary day (A ends the day B starts) count as overlapping — no shared days are
/// allowed. Returns the name of the first overlapping contract found, or null if none.
/// Composed live by the Contract Wizard's Basics step (UX convenience, disables Continue)
/// and by SealNenContractValidator (the real guard, defense in depth).
/// </summary>
public sealed record CheckNenContractDateRangeOverlapQuery(DateOnly StartDate, DateOnly EndDate) : IQuery<string?>;
