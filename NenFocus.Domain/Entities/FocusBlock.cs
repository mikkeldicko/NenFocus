using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Entities;

public sealed class FocusBlock : PlannedBlock
{
    /// <summary>
    /// A Focus block cannot exceed this many hours — Background blocks have no such
    /// limit. Focus-only by design, mirroring the already-established Focus-only Work
    /// Hours exception. Lives here (not a validator) because it's a pure fact about
    /// Start/End, the same justification already used for the entity's own end &gt; start
    /// guard — no repository lookup needed, so nothing pushes it out of Domain.
    /// </summary>
    public const int MaxDurationHours = 12;

    /// <summary>Null means the Ritual was never completed for this block.</summary>
    public DateTime? RitualCompletedAt { get; private set; }

    /// <summary>Null unless the session ended before this block's scheduled End.</summary>
    public DateTime? SessionEndedAt { get; private set; }

    private FocusBlock() { }

    public static FocusBlock Create(
        Guid id,
        Guid blockPresetId,
        DateTime start,
        DateTime end,
        string? title = null,
        Guid? contractId = null,
        IReadOnlyList<RedemptionEntry>? redemptionEntries = null)
    {
        if (end <= start)
            throw new ArgumentException("End time must be after start time.");
        ValidateDuration(start, end);

        return new FocusBlock
        {
            Id = id,
            BlockPresetId = blockPresetId,
            Start = start,
            End = end,
            Title = title,
            ContractId = contractId,
            RedemptionEntries = redemptionEntries ?? Array.Empty<RedemptionEntry>()
        };
    }

    public override void Reschedule(DateTime start, DateTime end)
    {
        ValidateDuration(start, end);
        base.Reschedule(start, end);
    }

    private static void ValidateDuration(DateTime start, DateTime end)
    {
        if (end - start > TimeSpan.FromHours(MaxDurationHours))
            throw new ArgumentException($"A Focus block cannot exceed {MaxDurationHours} hours.");
    }

    public void MarkRitualCompleted(DateTime completedAt)
    {
        if (RitualCompletedAt.HasValue)
            throw new InvalidOperationException(
                "Ritual has already been marked complete for this block.");

        RitualCompletedAt = completedAt;
    }

    public void MarkSessionEnded(DateTime endedAt)
    {
        if (SessionEndedAt.HasValue)
            throw new InvalidOperationException(
                "Session end has already been marked for this block.");

        SessionEndedAt = endedAt;
    }

    /// <summary>
    /// Tail freed once a session ended early; whole window freed once the block is genuinely
    /// Missed (ritual never completed and its scheduled End has already passed) — mirrors
    /// <see cref="Services.IFocusBlockOutcomeClassifier.Classify"/>'s own Missed condition so
    /// the two can never disagree about when a block is truly vacated. Still-pending or
    /// fully-used blocks keep the whole window reserved.
    /// </summary>
    public override DateTime EffectiveEnd(DateTime asOf)
    {
        if (SessionEndedAt is { } endedEarly) return endedEarly;
        if (RitualCompletedAt is null && asOf >= End) return Start;
        return End;
    }

    /// <summary>
    /// Replaces which missed/late blocks this redemption block covers. A plain setter, same
    /// as AssignPreset/AssignContract — same-preset, uncovered-deficit, and same-day
    /// constraints are Application-layer concerns, not enforced here.
    /// </summary>
    public void AssignRedemptionEntries(IReadOnlyList<RedemptionEntry> entries)
    {
        RedemptionEntries = entries;
    }

    /// <summary>
    /// True when this block's own outcome represents an unredeemed shortfall of its own —
    /// Missed, StartedLate, or EndedEarly — and it isn't itself already serving as a
    /// redemption for another block. A block actively covering someone else's deficit never
    /// becomes a deficit source in its own right (e.g. a Ritual that ran a few minutes past
    /// its own grace period, StartedLate against its own schedule): the commitment it exists
    /// to redeem is what's tracked, not a second one layered on top of it. Takes the outcome
    /// as a parameter rather than reclassifying — IFocusBlockOutcomeClassifier.Classify needs
    /// "now" and session data the caller already has, and the two must never disagree about
    /// what the outcome was.
    /// </summary>
    public bool CanContributeOwnDeficit(FocusBlockOutcome? outcome) =>
        outcome is FocusBlockOutcome.Missed or FocusBlockOutcome.StartedLate or FocusBlockOutcome.EndedEarly
        && RedemptionEntries.Count == 0;
}
