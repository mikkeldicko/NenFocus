using NenFocus.Domain.Enums;

namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// A Nen Contract's rules — every setting that carries no dates, so a ContractPreset can hold
/// the same value a sealed NenContract does (Gap 93). What needs the calendar (the days, each
/// day's floor, status) stays on the contract. A new contract rule is one field here and the
/// answer that reads it; preset, wizard and Seal carry it without changes.
/// </summary>
public sealed record ContractRules
{
    public ContractRules(
        StrictnessRule strictness,
        DailyFloor? fixedDailyFloor,
        bool allowCancellation,
        bool keepBlockingDuringBreaks,
        RedemptionPlacementPolicy redemptionPlacement,
        bool bonusBlocksFollowStrictness)
    {
        Strictness = strictness;
        FixedDailyFloor = fixedDailyFloor;
        AllowCancellation = allowCancellation;
        KeepBlockingDuringBreaks = keepBlockingDuringBreaks;
        RedemptionPlacement = redemptionPlacement;
        BonusBlocksFollowStrictness = bonusBlocksFollowStrictness;
    }

    // For EF only: it maps each property by name onto its owner's own columns (NenFocusDbContext).
    private ContractRules() { }

    public StrictnessRule Strictness { get; private set; }

    /// <summary>The same floor every day except rest days, or none — then <see cref="FloorRule"/> says where floors come from.</summary>
    public DailyFloor? FixedDailyFloor { get; private set; }

    /// <summary>Stored and shown, not enforced yet — Strict Mode is the intended gate (Gap 83).</summary>
    public bool AllowCancellation { get; private set; }

    /// <summary>Whether sessions of the contract's blocks keep their preset enforced through their breaks (Gap 64).</summary>
    public bool KeepBlockingDuringBreaks { get; private set; }

    /// <summary>Which days a redemption block may originate a new deficit claim on.</summary>
    public RedemptionPlacementPolicy RedemptionPlacement { get; private set; }

    /// <summary>
    /// Whether the contract's strictness also governs its Bonus blocks. Off: a future Bonus block
    /// is free, like a plain block. On: it is treated as a Committed block is, except for the floor.
    /// </summary>
    public bool BonusBlocksFollowStrictness { get; private set; }

    /// <summary>
    /// How the contract's days get their floor when they are generated: the fixed floor when one
    /// is set; otherwise each day follows its plan — except under Period Time Budget, which then
    /// has no per-day obligation at all, only a whole-period target. Worked out, never stored,
    /// so the strictness and the floor rule can't disagree.
    /// </summary>
    /// <summary>
    /// How far a block the contract binds may move — the one question the strictness answers,
    /// and the one place it is asked. A new strictness level without a line here is a compiler
    /// warning.
    /// </summary>
#pragma warning disable CS8524 // no arm for an unnamed value on purpose: a discard arm would also hide a new level (CS8509)
    public MovementRange MovementRange => Strictness switch
    {
        StrictnessRule.Locked => MovementRange.None,
        StrictnessRule.DailyFlexibility => MovementRange.OwnDay,
        StrictnessRule.PeriodTimeBudget => MovementRange.WholePeriod
    };
#pragma warning restore CS8524

    public DailyFloorRule FloorRule =>
        FixedDailyFloor is { } floor ? new DailyFloorRule.Fixed(floor)
        : Strictness == StrictnessRule.PeriodTimeBudget ? new DailyFloorRule.None()
        : new DailyFloorRule.FollowsPlan();
}
