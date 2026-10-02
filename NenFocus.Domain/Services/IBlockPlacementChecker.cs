using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Services;

/// <summary>
/// The one placement rulebook (Gap 88). <see cref="Check"/> is the full question "may a block go
/// here?" — not in the past → allowed by the contract that governs it → free for Focus;
/// <see cref="CheckFree"/> is just the last step, "is the time free".
/// </summary>
public interface IBlockPlacementChecker
{
    /// <summary>
    /// In order, first failure wins: <paramref name="slot"/> has started → InThePast; the
    /// governing contract's <c>AllowsBlockPlacement</c> is false → OutsideContractHours; then,
    /// Focus only, <see cref="CheckFree"/>. An allowed verdict names the governing contract.
    /// Which contract governs (Gap 97): a block that is already Committed or Redemption
    /// (<paramref name="membership"/>) is held to its own contract — it never leaves it; a
    /// plain or Bonus block is governed by <see cref="GoverningContract"/>, where the slot sits.
    /// <paramref name="contractsInEffect"/> are the only contracts that govern anything.
    /// <paramref name="blocks"/> and <paramref name="sessions"/> are the calendar as it will be.
    /// </summary>
    PlacementVerdict Check(TimeSlot slot, BlockPresetType type, ContractMembership? membership,
        IEnumerable<NenContract> contractsInEffect,
        IEnumerable<PlannedBlock> blocks, IEnumerable<Session> sessions, Moment asOf, UserTimeZone timeZone);

    /// <summary>
    /// The contract whose planned period <paramref name="slot"/> touches, or null — the contract
    /// a plain block placed there would belong to. Contracts never share a day, so there is at
    /// most one that could also allow it.
    /// </summary>
    NenContract? GoverningContract(TimeSlot slot, IEnumerable<NenContract> contractsInEffect, UserTimeZone timeZone);

    /// <summary>
    /// Is this time free for Focus? Allowed, or TimeTaken with the first Focus time in the way
    /// (earliest first): a Focus block's <see cref="PlannedBlock.ReservedSlot"/> as of
    /// <paramref name="asOf"/>, or a running session's <see cref="Session.ReservedSlot"/>.
    /// Background blocks never clash. <paramref name="blocks"/> and <paramref name="sessions"/>
    /// are the calendar as it will be — leave out a block that is being moved or replaced.
    /// </summary>
    PlacementVerdict CheckFree(TimeSlot slot, IEnumerable<PlannedBlock> blocks, IEnumerable<Session> sessions, Moment asOf);
}
