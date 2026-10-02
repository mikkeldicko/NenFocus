using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Services;

/// <summary>
/// The one answer to "may this existing block be changed or deleted?" (Gap 93) — the sibling of
/// <see cref="IBlockPlacementChecker"/>'s "may a block go here?". It holds no rule of its own:
/// it asks the block (its own lock), then the block's contract, and the first refusal wins.
/// <c>contract</c> is always the block's own contract, or null for a block in none; a member
/// whose contract is gone is treated as history. Asked before placement, so a running block
/// answers "it has started", not "that time is in the past".
/// </summary>
public interface IBlockChangeChecker
{
    /// <summary>What may be done to the block at all — for a screen deciding what to offer.</summary>
    BlockChangeAllowances AllowancesFor(PlannedBlock block, NenContract? contract, Moment asOf);

    /// <summary>
    /// May the block take <paramref name="newSlot"/> (a move or a resize), and its preset change
    /// if <paramref name="presetChanges"/>? Only what actually changes is asked about. A block
    /// that counts toward the floor must leave the floor met: <paramref name="contractBlocks"/>
    /// are the contract's blocks as they are now.
    /// </summary>
    ChangeVerdict CheckReschedule(PlannedBlock block, TimeSlot newSlot, bool presetChanges,
        NenContract? contract, IEnumerable<PlannedBlock> contractBlocks, Moment asOf, UserTimeZone timeZone);

    /// <summary>
    /// May the block be replaced by <paramref name="first"/> and <paramref name="second"/>? Each
    /// half is held to the same movement rule as a move, and the floor must be met with both in place.
    /// </summary>
    ChangeVerdict CheckSplit(PlannedBlock block, TimeSlot first, TimeSlot second,
        NenContract? contract, IEnumerable<PlannedBlock> contractBlocks, Moment asOf, UserTimeZone timeZone);

    /// <summary>May the block be deleted?</summary>
    ChangeVerdict CheckDelete(PlannedBlock block, NenContract? contract, Moment asOf);
}
