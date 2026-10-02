namespace NenFocus.Domain.Enums;

/// <summary>
/// Whether a change to an existing block (move, resize, preset, split, delete) may happen, or the
/// first reason it may not (Gap 93). A reason, never a sentence — the wording is built in one
/// place in the Application layer.
/// </summary>
public enum ChangeOutcome
{
    Allowed,

    /// <summary>The block has started (running or ended): it can no longer be changed.</summary>
    BlockHasStarted,

    /// <summary>The block is running right now: it can't be deleted until it ends.</summary>
    BlockIsRunning,

    /// <summary>The block's contract is Completed or Broken: its blocks are history.</summary>
    ContractIsOver,

    /// <summary>The block is part of a contract's record and is never deleted.</summary>
    ContractBlockNeverDeleted,

    /// <summary>The contract's strictness lets a bound block move nowhere.</summary>
    NoMovementAllowed,

    /// <summary>The contract's strictness keeps a bound block on its own day.</summary>
    OutsideOwnDay,

    /// <summary>A contract's block never leaves the contract's planned period.</summary>
    OutsideContractPeriod,

    /// <summary>The contract binds this block, so its preset stays what it was.</summary>
    PresetFixed,

    /// <summary>Split is only for a Focus block that a contract binds.</summary>
    SplitNotOffered,

    /// <summary>The change would leave a day under its floor.</summary>
    BelowDailyFloor,

    /// <summary>The change would leave the contract under its whole-period target.</summary>
    BelowPeriodTarget
}
