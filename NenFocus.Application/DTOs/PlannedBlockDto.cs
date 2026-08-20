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

    public int DurationMinutes => (int)(End - Start).TotalMinutes;
}
