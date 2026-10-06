using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Services;

public sealed class BlockChangeChecker : IBlockChangeChecker
{
    private readonly IPlannedFocusHoursCalculator _plannedHours;

    public BlockChangeChecker(IPlannedFocusHoursCalculator plannedHours)
    {
        _plannedHours = plannedHours;
    }

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

    public ChangeVerdict CheckReschedule(PlannedBlock block, TimeSlot newSlot, bool presetChanges,
        NenContract? contract, IEnumerable<PlannedBlock> contractBlocks, Moment asOf, UserTimeZone timeZone)
    {
        if (block.CheckOwnChange(asOf) is { IsAllowed: false } locked)
            return locked;

        if (block.Membership is null)
            return ChangeVerdict.Allowed;

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

        return contract.CheckFloor(_plannedHours.Calculate(focusTime, timeZone));
    }
}
