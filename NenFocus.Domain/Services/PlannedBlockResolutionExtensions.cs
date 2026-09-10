using NenFocus.Domain.Entities;

namespace NenFocus.Domain.Services;

public static class PlannedBlockResolutionExtensions
{
    /// <summary>
    /// Has this block's own story concluded — the single answer both contract-completion
    /// readiness and contract-break eligibility need for "should this block still be
    /// treated as outstanding." For a FocusBlock this defers to
    /// <see cref="IFocusBlockOutcomeClassifier.Classify"/> (the same source of truth
    /// GetNenContractRecapQueryHandler, GetNenContractDeficitsQueryHandler, and others
    /// already use) so a block that ended its session early is correctly resolved well
    /// before its raw scheduled End — Classify doesn't even need `asOf` once
    /// ritualCompletedAt/sessionEndedAt are fixed, so the answer is a permanent fact, not a
    /// "is this live right now" check. A BackgroundBlock has no ritual/session to account
    /// for, so it falls back to the unchanged, already-correct HasEnded.
    /// </summary>
    public static bool IsResolved(this PlannedBlock block, IFocusBlockOutcomeClassifier classifier, DateTime asOf)
        => block is FocusBlock fb
            ? classifier.Classify(fb.Start, fb.End, fb.RitualCompletedAt, fb.SessionEndedAt, asOf) is not null
            : block.HasEnded(asOf);
}
