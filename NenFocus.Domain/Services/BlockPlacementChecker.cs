using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Services;

/// <summary>
/// The one placement rulebook (Gap 88). <see cref="Check"/> is the full question "may a block go
/// here?" — not in the past → allowed by the contract that governs it → free for Focus;
/// <see cref="CheckFree"/> is just the last step, "is the time free".
/// </summary>
public sealed class BlockPlacementChecker
{
    /// <summary>
    /// In order, first failure wins: <paramref name="slot"/> has started → InThePast; the
    /// governing contract's <c>AllowsBlockPlacement</c> is false → OutsideContractHours; then,
    /// Focus only, <see cref="CheckFree"/>. An allowed verdict names the governing contract.
    /// Which contract governs (Gap 97): a block with a <paramref name="membership"/>, whatever its role,
    /// is held to its own contract — it never leaves it (Gap 106); a plain block is governed by
    /// <see cref="GoverningContract"/>, where the slot sits.
    /// <paramref name="contractsInEffect"/> are the only contracts that govern anything.
    /// <paramref name="blocks"/> are the calendar as it will be; <paramref name="running"/> is the session running now, if any — at most one runs (Gap 92).
    /// </summary>
    public PlacementVerdict Check(TimeSlot slot, BlockPresetType type, ContractMembership? membership,
        IEnumerable<NenContract> contractsInEffect,
        IEnumerable<PlannedBlock> blocks, Session? running, Moment asOf, UserTimeZone timeZone)
    {
        if (slot.HasStarted(asOf))
            return PlacementVerdict.Refused(PlacementOutcome.InThePast);

        var contracts = contractsInEffect.Where(c => c.IsInEffect).ToList();

        // A block with a contract id is judged against its own contract, whatever its role (Gap 106,
        // decision 17); a contract no longer in effect governs nothing.
        var governing = membership is not null
            ? contracts.FirstOrDefault(c => c.Id == membership.ContractId)
            : GoverningContract(slot, contracts, timeZone);

        if (governing is not null && !governing.AllowsBlockPlacement(slot, timeZone))
            return PlacementVerdict.Refused(PlacementOutcome.OutsideContractHours);

        if (type == BlockPresetType.Focus && CheckFree(slot, blocks, running, asOf) is { Outcome: not PlacementOutcome.Allowed } taken)
            return taken;

        return PlacementVerdict.AllowedUnder(governing?.Id);
    }

    /// <summary>
    /// The contract whose planned period <paramref name="slot"/> touches, or null — the contract
    /// a plain block placed there would belong to. Contracts never share a day, so there is at
    /// most one that could also allow it.
    /// </summary>
    public NenContract? GoverningContract(TimeSlot slot, IEnumerable<NenContract> contractsInEffect, UserTimeZone timeZone)
        => contractsInEffect.FirstOrDefault(c => c.Governs(slot, timeZone));

    /// <summary>
    /// Is this time free for Focus? Allowed, or TimeTaken with the first Focus time in the way
    /// (earliest first): a Focus block's <see cref="PlannedBlock.ReservedSlot"/> as of
    /// <paramref name="asOf"/>, or a running session's <see cref="Session.ReservedSlot"/>.
    /// Background blocks never clash. <paramref name="blocks"/> are the calendar as it will be, <paramref name="running"/> the session
    /// running now (at most one, Gap 92) — leave out a block that is being moved or replaced.
    /// </summary>
    public PlacementVerdict CheckFree(TimeSlot slot, IEnumerable<PlannedBlock> blocks, Session? running, Moment asOf)
    {
        var focusTime = blocks.OfType<FocusBlock>().Select(b => b.ReservedSlot(asOf))
            .Append(running?.ReservedSlot);

        var firstInTheWay = focusTime
            .OfType<TimeSlot>()
            .Where(slot.Overlaps)
            .Order()
            .FirstOrDefault();

        return firstInTheWay is null
            ? PlacementVerdict.Allowed
            : PlacementVerdict.Refused(PlacementOutcome.TimeTaken, firstInTheWay);
    }
}
