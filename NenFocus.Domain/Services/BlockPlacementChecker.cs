using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Services;

public sealed class BlockPlacementChecker : IBlockPlacementChecker
{
    public PlacementVerdict Check(TimeSlot slot, BlockPresetType type, NenContract? contract,
        IEnumerable<PlannedBlock> blocks, IEnumerable<Session> sessions, Moment asOf, UserTimeZone timeZone)
    {
        if (slot.HasStarted(asOf))
            return PlacementVerdict.Refused(PlacementOutcome.InThePast);

        if (contract is not null && !contract.AllowsBlockPlacement(slot, timeZone))
            return PlacementVerdict.Refused(PlacementOutcome.OutsideContractHours);

        if (type == BlockPresetType.Focus)
            return CheckFree(slot, blocks, sessions, asOf);

        return PlacementVerdict.Allowed;
    }

    public PlacementVerdict CheckFree(TimeSlot slot, IEnumerable<PlannedBlock> blocks, IEnumerable<Session> sessions, Moment asOf)
    {
        var focusTime = blocks.OfType<FocusBlock>().Select(b => b.ReservedSlot(asOf))
            .Concat(sessions.Select(s => s.ReservedSlot));

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
