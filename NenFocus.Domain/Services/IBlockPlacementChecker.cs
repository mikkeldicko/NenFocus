using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Services;

/// <summary>
/// The one placement rulebook (Gap 88). <see cref="Check"/> is the full question "may a block go
/// here?" — not in the past → inside the contract's window → free for Focus; <see cref="CheckFree"/>
/// is just the last step, "is the time free".
/// </summary>
public interface IBlockPlacementChecker
{
    /// <summary>
    /// In order, first failure wins: <paramref name="slot"/> has started → InThePast;
    /// <paramref name="contract"/>'s <c>AllowsBlockPlacement</c> is false → OutsideContractHours;
    /// then, Focus only, <see cref="CheckFree"/>. <paramref name="blocks"/> and
    /// <paramref name="sessions"/> are the calendar as it will be.
    /// </summary>
    PlacementVerdict Check(TimeSlot slot, BlockPresetType type, NenContract? contract,
        IEnumerable<PlannedBlock> blocks, IEnumerable<Session> sessions, Moment asOf, UserTimeZone timeZone);

    /// <summary>
    /// Is this time free for Focus? Allowed, or TimeTaken with the first Focus time in the way
    /// (earliest first): a Focus block's <see cref="PlannedBlock.ReservedSlot"/> as of
    /// <paramref name="asOf"/>, or a running session's <see cref="Session.ReservedSlot"/>.
    /// Background blocks never clash. <paramref name="blocks"/> and <paramref name="sessions"/>
    /// are the calendar as it will be — leave out a block that is being moved or replaced.
    /// </summary>
    PlacementVerdict CheckFree(TimeSlot slot, IEnumerable<PlannedBlock> blocks, IEnumerable<Session> sessions, Moment asOf);
}
