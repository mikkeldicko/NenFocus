using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

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
    public TimeSlot Shown { get; init; } = null!;

    public bool IsCompleted { get; init; }

    public Guid? ContractId { get; init; }

    /// <summary>
    /// PlannedBlock.HasEnded(now). The Planner uses it to pick the read-only snapshot over the
    /// edit form for a Background block; what may be done to the block is <see cref="Allowances"/>.
    /// </summary>
    public bool HasEnded { get; init; }

    /// <summary>
    /// What may be done to this block at all — BlockChangeChecker's answer, the same one the
    /// Update, Split and Delete validators ask (Gap 93). The Planner offers a drag, a button or
    /// an editable field from these and never works a lock out itself; whether one concrete
    /// change is allowed is still answered when it is saved.
    /// </summary>
    public required BlockChangeAllowances Allowances { get; init; }

    /// <summary>
    /// FocusBlock.Outcome for a FocusBlock — null for a
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
    /// The Focus block's stored role in its contract (Gap 97): Committed (drafted and sealed),
    /// Redemption (placed to cover another block's deficit) or Bonus (placed on the contract
    /// for any other reason — never counts toward its floor/Day-Met scoring). A block has one
    /// role, so only one applies; null is a block of no contract, or a Background block.
    /// Drives the Planner tile's badge.
    /// </summary>
    public PlannedBlockContractBadge? ContractBadge { get; init; }

    /// <summary>The covering contract's Name when ContractBadge is Bonus, otherwise null.</summary>
    public string? BonusContractName { get; init; }

    public int DurationMinutes => (int)Shown.Length.TotalMinutes;

    /// <summary>
    /// Same span as DurationMinutes, as a TimeSpan — lets the Planner snapshot view bind
    /// through DurationToStringConverter without the ViewModel doing its own TimeSpan math
    /// (the ViewModel Forbidden Zone's "No Math" rule).
    /// </summary>
    public TimeSpan Duration => Shown.Length;
}
