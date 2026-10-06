using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.DTOs;

/// <summary>
/// Whether a Nen Contract may be completed early via the "Complete Contract" button, and why
/// not — the contract's own answer (<c>NenContract.CheckComplete</c>, Gap 95) as the Planner,
/// the validator and the handler read it.
/// </summary>
public sealed record NenContractCompletionReadinessDto
{
    public bool IsReady { get; init; }

    /// <summary>Committed hours not yet delivered, clamped to 0 (never negative — an
    /// over-delivered contract still reports 0 remaining, not a negative surplus).</summary>
    public double RemainingHours { get; init; }

    /// <summary>Blocks of the contract not yet done — every member, Bonus blocks included.
    /// Zero when ready.</summary>
    public int PendingBlockCount { get; init; }

    /// <summary>The sentence saying why not, or null when ready.</summary>
    public string? NotReadyReason { get; init; }

    /// <summary>Not ready because the contract isn't Active (or doesn't exist).</summary>
    public static NenContractCompletionReadinessDto NotActive { get; } =
        new() { IsReady = false, NotReadyReason = "Only an active contract can be completed." };

    public static NenContractCompletionReadinessDto From(ContractEndVerdict verdict) => new()
    {
        IsReady = verdict.IsAllowed,
        RemainingHours = verdict.RemainingHours,
        PendingBlockCount = verdict.PendingBlockCount,
        NotReadyReason = ContractEndVerdictDto.From(verdict).Message
    };
}
