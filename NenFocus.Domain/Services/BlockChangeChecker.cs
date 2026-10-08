using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Services;

/// <summary>
/// The one answer to "may this existing block be changed or deleted?" (Gap 93) — the sibling of
/// <see cref="BlockPlacementChecker"/>'s "may a block go here?". It holds no rule of its own:
/// it asks the block (its own lock), then the block's contract, and the first refusal wins.
/// <c>contract</c> is always the block's own contract, or null for a block in none; a member
/// whose contract is gone is treated as history. Asked before placement, so a running block
/// answers "it has started", not "that time is in the past".
/// </summary>
public sealed class BlockChangeChecker
{
    /// <summary>What may be done to the block at all — for a screen deciding what to offer.</summary>
    public BlockChangeAllowances AllowancesFor(PlannedBlock block, NenContract? contract, Moment asOf)
    {
        var mayChange = block.CheckOwnChange(asOf).IsAllowed;
        var isMember = block.Membership is not null;

        var canSplit = isMember && block.CheckOwnSplit(asOf).IsAllowed && (contract?.CheckBlockSplit(block).IsAllowed ?? false);

        return new BlockChangeAllowances(
            CanReschedule: mayChange && (!isMember || (contract?.CheckBlockMayMove(block).IsAllowed ?? false)),
            CanChangePreset: mayChange && (!isMember || (contract?.CheckBlockPresetChange(block).IsAllowed ?? false)),
            CanSplit: canSplit,
            CanDelete: CheckDelete(block, contract, asOf).IsAllowed)
        {
            SplitPiecesMayChangeDay = canSplit && contract!.AllowsBlockOnAnotherDay(block)
        };
    }

    /// <summary>
    /// May the block take <paramref name="newSlot"/> (a move or a resize), and its preset change
    /// if <paramref name="presetChanges"/>? Only what actually changes is asked about. A block
    /// that counts toward the floor must leave the floor met: <paramref name="contractBlocks"/>
    /// are the contract's blocks as they are now.
    /// A plain block may not move where any of <paramref name="contractsInEffect"/> governs — a drag
    /// never changes membership (Gap 106); a member ignores the list. A list, so no caller picks the contract.
    /// </summary>
    public ChangeVerdict CheckReschedule(PlannedBlock block, TimeSlot newSlot, bool presetChanges,
        NenContract? contract, IEnumerable<PlannedBlock> contractBlocks, IEnumerable<NenContract> contractsInEffect,
        Moment asOf, UserTimeZone timeZone)
    {
        if (block.CheckOwnChange(asOf) is { IsAllowed: false } locked)
            return locked;

        // A drag never changes membership (Gap 106, decision 18): a plain block may not move where a
        // contract governs. Changing only its preset in place is not a move.
        if (block.Membership is null)
            return newSlot != block.Scheduled && contractsInEffect.Any(c => c.Governs(newSlot, timeZone))
                ? ChangeVerdict.Refused(ChangeOutcome.WouldJoinContract)
                : ChangeVerdict.Allowed;

        if (contract is null)
            return ChangeVerdict.Refused(ChangeOutcome.ContractIsOver);

        if (presetChanges && contract.CheckBlockPresetChange(block) is { IsAllowed: false } presetFixed)
            return presetFixed;

        if (newSlot == block.Scheduled)
            return ChangeVerdict.Allowed;

        if (contract.CheckBlockLength(block, newSlot) is { IsAllowed: false } lengthFixed)
            return lengthFixed;

        if (contract.CheckBlockMove(block, newSlot, timeZone) is { IsAllowed: false } cannotMove)
            return cannotMove;

        return CheckFloor(block, [newSlot], contract, contractBlocks, timeZone);
    }

    /// <summary>
    /// May the block be replaced by <paramref name="first"/> and <paramref name="second"/>? Each
    /// half is held to the same movement rule as a move, and the floor must be met with both in place.
    /// </summary>
    public ChangeVerdict CheckSplit(PlannedBlock block, TimeSlot first, TimeSlot second,
        NenContract? contract, IEnumerable<PlannedBlock> contractBlocks, Moment asOf, UserTimeZone timeZone)
    {
        if (block.CheckOwnSplit(asOf) is { IsAllowed: false } notSplittable)
            return notSplittable;

        if (block.Membership is null)
            return ChangeVerdict.Refused(ChangeOutcome.SplitNotOffered);

        if (contract is null)
            return ChangeVerdict.Refused(ChangeOutcome.ContractIsOver);

        if (contract.CheckBlockSplit(block) is { IsAllowed: false } noSplit)
            return noSplit;

        foreach (var half in new[] { first, second })
        {
            if (contract.CheckBlockMove(block, half, timeZone) is { IsAllowed: false } cannotMove)
                return cannotMove;
        }

        return CheckFloor(block, [first, second], contract, contractBlocks, timeZone);
    }

    /// <summary>May the block be deleted?</summary>
    public ChangeVerdict CheckDelete(PlannedBlock block, NenContract? contract, Moment asOf)
    {
        if (block.CheckOwnDelete(asOf) is { IsAllowed: false } running)
            return running;

        if (block.Membership is null)
            return ChangeVerdict.Allowed;

        return contract?.CheckBlockDelete(block, asOf)
            ?? ChangeVerdict.Refused(ChangeOutcome.ContractBlockNeverDeleted);
    }

    /// <summary>
    /// The floor is a minimum of what was signed (Gap 95): only a Committed block's change can lower
    /// it, and the plan after the change is every other Committed Focus block as it stands, plus
    /// where this one goes. A Redemption block pays for a missed block, never for a shrunk one.
    /// </summary>
    private ChangeVerdict CheckFloor(PlannedBlock block, IReadOnlyList<TimeSlot> replacement,
        NenContract contract, IEnumerable<PlannedBlock> contractBlocks, UserTimeZone timeZone)
    {
        if (block is not FocusBlock { IsCommitted: true })
            return ChangeVerdict.Allowed;

        var focusTime = contractBlocks
            .OfType<FocusBlock>()
            .Where(b => b.IsCommitted && b.Id != block.Id)
            .Select(b => b.Scheduled)
            .Concat(replacement);

        return contract.CheckFloor(timeZone.HoursPerDay(focusTime));
    }
}
