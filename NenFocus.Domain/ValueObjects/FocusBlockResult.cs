using NenFocus.Domain.Enums;

namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// A Focus block's frozen result (Gap 114, D5): the verdict and the worked span, written once by
/// the block's own final transition — its session ended, it was missed, or it was cancelled — and
/// never recomputed. A snapshot, so a later rule change (the grace period, the outcome priority)
/// never re-grades a past block. <see cref="Outcome"/> is the stored verdict (FHIR's <c>status</c>:
/// the only thing that tells <c>Missed</c> from <c>Cancelled</c>, since both have no work);
/// <see cref="WorkSlot"/> is the one grace-dependent fact, null when nothing ran. StartedLate,
/// EndedEarly and Credit are derived from WorkSlot, not stored (FHIR: store the fact and the
/// verdict, derive the pure arithmetic).
/// </summary>
public sealed record FocusBlockResult
{
    public FocusBlockOutcome Outcome { get; init; }
    public TimeSlot? WorkSlot { get; init; }

    public FocusBlockResult(FocusBlockOutcome outcome, TimeSlot? workSlot)
    {
        Outcome = outcome;
        WorkSlot = workSlot;
    }

    // EF only: WorkSlot is an owned navigation, so the record's constructor can't bind it —
    // EF materializes through this and the init setters instead.
    private FocusBlockResult() { }
}
