using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Services;

public sealed class BlockPlacementChecker : IBlockPlacementChecker
{
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

    public NenContract? GoverningContract(TimeSlot slot, IEnumerable<NenContract> contractsInEffect, UserTimeZone timeZone)
        => contractsInEffect.FirstOrDefault(c => c.Governs(slot, timeZone));

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
