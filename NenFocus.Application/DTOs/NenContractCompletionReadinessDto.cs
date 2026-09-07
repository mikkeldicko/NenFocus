namespace NenFocus.Application.DTOs;

/// <summary>
/// Whether a Nen Contract is ready to be completed early via the "Complete Contract"
/// button, plus enough structure to explain why not — mirrors the CheckRedemptionEligibility
/// precedent of returning real structure rather than a bare bool, so a future disabled-state
/// tooltip never has to re-derive "why not ready" from scratch.
/// </summary>
public sealed record NenContractCompletionReadinessDto
{
    public bool IsReady { get; init; }

    /// <summary>Committed hours not yet delivered, clamped to 0 (never negative — an
    /// over-delivered contract still reports 0 remaining, not a negative surplus).</summary>
    public double RemainingHours { get; init; }

    /// <summary>Still-open blocks anywhere in the contract's window — committed, or an
    /// unlinked Bonus-eligible block within [StartsAt, EndsAt]. Zero when ready.</summary>
    public int PendingBlockCount { get; init; }
}
