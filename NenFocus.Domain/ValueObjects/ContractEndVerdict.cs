using NenFocus.Domain.Enums;

namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// The answer to "may this contract end now?": allowed, or the first reason not — with the
/// committed hours still owed and the blocks not yet done, which completing early reports either
/// way (Gap 95).
/// </summary>
public sealed record ContractEndVerdict(ContractEndOutcome Outcome, double RemainingHours = 0, int PendingBlockCount = 0)
{
    public bool IsAllowed => Outcome == ContractEndOutcome.Allowed;
}
