using NenFocus.Domain.Enums;

namespace NenFocus.Application.DTOs;

public sealed record PlannedBlockDto
{
    public Guid Id { get; init; }
    public Guid PresetId { get; init; }
    public string PresetName { get; init; } = string.Empty;

    public BlockPresetType BlockPresetType { get; init; }
    public WebsiteFilterType WebsiteFilterType { get; init; }

    /// <summary>
    /// Display span, not the raw scheduled window: pulled forward to RitualCompletedAt for a
    /// StartedLate block and back to SessionEndedAt for an EndedEarly one, so the tile always
    /// reflects the actual worked window. A grace-period-late start is left untrimmed, since
    /// it's credited as full duration.
    /// </summary>
    public DateTime Start { get; init; }
    public DateTime End { get; init; }

    public bool IsCompleted { get; init; }

    /// <summary>
    /// Mirrors PlannedBlock.HasEnded(now) — the same predicate DeletePlannedBlockValidator
    /// enforces. The UI binds to this rather than deriving its own notion of "past" from
    /// Start/End, so the two can never drift apart.
    /// </summary>
    public Guid? ContractId { get; init; }
    public bool HasEnded { get; init; }

    /// <summary>
    /// True when ContractId resolves to a contract that is still a live commitment
    /// (NenContract.IsInEffect — Scheduled or Active). Mirrors DeletePlannedBlockValidator/
    /// UpdatePlannedBlockValidator's own resolution exactly, so the UI never re-derives
    /// "is this block locked by its contract" independently of the Application-layer rule.
    /// </summary>
    public bool IsUnderInEffectContract { get; init; }

    /// <summary>
    /// The owning contract's StrictnessRule, or null when ContractId is null or doesn't
    /// resolve. Drives Split Block eligibility on the Planner (Daily Flexibility only) —
    /// the UI never re-derives strictness independently of this Application-computed value.
    /// </summary>
    public StrictnessRule? ContractStrictnessRule { get; init; }

    /// <summary>
    /// FocusBlockOutcomeClassifier.Classify's result for a FocusBlock — null for a
    /// BackgroundBlock (no ritual, no outcome concept) or a FocusBlock still pending. Drives
    /// the Planner tile's Missed/StartedLate badge; a Missed block still renders at its full
    /// original span (nothing new can practically be scheduled over already-passed time), but
    /// StartedLate/EndedEarly are also reflected in Start/End above.
    /// </summary>
    public FocusBlockOutcome? Outcome { get; init; }

    /// <summary>
    /// True when the ritual completed past the grace period, independent of Outcome. Classify
    /// gives EndedEarly priority over StartedLate — a block can only carry one Outcome — but a
    /// block can genuinely be both, and this flag lets the tile badge say so even when Outcome
    /// alone (EndedEarly) would hide the lateness.
    /// </summary>
    public bool WasStartedLate { get; init; }

    /// <summary>
    /// Redemption (a Focus block created to cover another block's deficit — RedemptionEntries
    /// non-empty, always linked) or Bonus (ContractId null but Start's date falls within some
    /// contract's [StartDate, EndDate], regardless of that contract's status — a purely
    /// read-time label, never counts toward that contract's floor/Day-Met scoring). The two
    /// are mutually exclusive by construction, so null covers both an ordinary committed
    /// block and one with no contract relation at all — both already fully distinguished by
    /// ContractId's own nullability. Drives the Planner tile's badge.
    /// </summary>
    public PlannedBlockContractBadge? ContractBadge { get; init; }

    /// <summary>The covering contract's Name when ContractBadge is Bonus, otherwise null.</summary>
    public string? BonusContractName { get; init; }

    /// <summary>
    /// How many seconds this block ended before its own scheduled end, when that shortfall
    /// was small enough to be forgiven outright (FocusBlockOutcomeClassifier.
    /// MinimumMeaningfulDeficitMinutes, Gap 45) — null whenever Outcome isn't EndedEarly, or
    /// when it is but the shortfall exceeded that threshold and so wasn't forgiven. Drives
    /// the Block Snapshot's "ended Ns early — full credit given" note; the tile's own ENDED
    /// EARLY badge is unaffected by this field — the badge stays honest about what happened,
    /// this just explains that it didn't cost anything.
    /// </summary>
    public double? EndedEarlyGraceSeconds { get; init; }

    public int DurationMinutes => (int)(End - Start).TotalMinutes;

    /// <summary>
    /// Same span as DurationMinutes, as a TimeSpan — lets the Planner snapshot view bind
    /// through DurationToStringConverter without the ViewModel doing its own TimeSpan math
    /// (the ViewModel Forbidden Zone's "No Math" rule).
    /// </summary>
    public TimeSpan Duration => End - Start;
}
