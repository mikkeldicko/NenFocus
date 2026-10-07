using NenFocus.Domain.Enums;

namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// The answer to "may this lifecycle action happen to the contract now?": allowed, or the first
/// reason not. Completing early reports the committed hours still owed and the blocks not yet done
/// with any refusal (Gap 95); allowed carries neither, so it can't be built claiming hours are
/// still owed (Gap 92, R2).
/// </summary>
public sealed record ContractActionVerdict
{
    private ContractActionVerdict(ContractActionOutcome outcome, double remainingHours, int pendingBlockCount)
    {
        Outcome = outcome;
        RemainingHours = remainingHours;
        PendingBlockCount = pendingBlockCount;
    }

    public ContractActionOutcome Outcome { get; }
    public double RemainingHours { get; }
    public int PendingBlockCount { get; }

    public bool IsAllowed => Outcome == ContractActionOutcome.Allowed;

    public static ContractActionVerdict Allowed { get; } = new(ContractActionOutcome.Allowed, 0, 0);

    public static ContractActionVerdict Refused(ContractActionOutcome outcome, double remainingHours = 0, int pendingBlockCount = 0)
    {
        if (outcome == ContractActionOutcome.Allowed)
            throw new ArgumentException("Use ContractActionVerdict.Allowed.", nameof(outcome));
        return new(outcome, remainingHours, pendingBlockCount);
    }
}
