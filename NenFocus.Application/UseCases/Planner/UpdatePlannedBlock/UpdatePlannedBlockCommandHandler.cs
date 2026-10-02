using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeRedemptionAttribution;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;

public sealed class UpdatePlannedBlockCommandHandler : ICommandHandler<UpdatePlannedBlockCommand, UpdatePlannedBlockResult>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockPresetRepository _presets;
    private readonly INenContractRepository _contracts;
    private readonly IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> _computeRedemptionAttribution;
    private readonly IBlockPlacementChecker _placement;
    private readonly IBlockChangeChecker _changes;
    private readonly ISystemClock _clock;

    public UpdatePlannedBlockCommandHandler(
        IPlannedBlockRepository blocks,
        IBlockPresetRepository presets,
        INenContractRepository contracts,
        IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> computeRedemptionAttribution,
        IBlockPlacementChecker placement,
        IBlockChangeChecker changes,
        ISystemClock clock)
    {
        _blocks = blocks;
        _presets = presets;
        _contracts = contracts;
        _computeRedemptionAttribution = computeRedemptionAttribution;
        _placement = placement;
        _changes = changes;
        _clock = clock;
    }

    public async Task<UpdatePlannedBlockResult> HandleAsync(UpdatePlannedBlockCommand command, CancellationToken cancellationToken = default)
    {
        var slot = command.Slot;

        var existing = await _blocks.GetByIdAsync(command.BlockId, cancellationToken);
        if (existing is null)
            return UpdatePlannedBlockResult.Fail($"Planned block '{command.BlockId}' not found.");

        var preset = await _presets.GetByIdAsync(command.PresetId, cancellationToken);
        if (preset is null)
            return UpdatePlannedBlockResult.Fail($"Preset '{command.PresetId}' not found.");

        var redemptionCapped = false;

        // Only a redemption block's coverage is recomputed on an edit, against its own contract
        // (Gap 97): a role never changes, so a Committed, Bonus or plain block never starts
        // claiming a deficit because it was edited. Attribution returns the final (capped)
        // window — the one place the cap is computed, so the validator's placement check and
        // this save can never disagree (Gap 88 phase 8).
        if (existing is FocusBlock { IsRedemption: true } redemption)
        {
            var existingEntries = redemption.RedemptionEntries
                .Select(RedemptionEntryDto.From)
                .ToList();

            var attribution = await _computeRedemptionAttribution.HandleAsync(
                new ComputeRedemptionAttributionQuery(redemption.Membership!.ContractId, command.PresetId, slot, existing.Id, existingEntries),
                cancellationToken);

            // Attribution may have shortened the slot after the validator approved the longer one —
            // the permission answer (the floor, above all) must hold for the slot that is saved.
            if (attribution.Slot != slot)
            {
                var contractId = redemption.Membership.ContractId;
                var change = _changes.CheckReschedule(redemption, attribution.Slot, presetChanges: false,
                    await _contracts.GetByIdAsync(contractId, cancellationToken),
                    await _blocks.GetForContractAsync(contractId, cancellationToken), _clock.Now, _clock.TimeZone);
                if (!change.IsAllowed)
                    return UpdatePlannedBlockResult.Fail(BlockChangeVerdictDto.From(change).Message!);
            }

            redemption.AssignRedemptionEntries(attribution.Entries
                .Select(e => new RedemptionEntry(e.MissedBlockId, e.MinutesCovered))
                .ToList());
            slot = attribution.Slot;
            redemptionCapped = attribution.WasCapped;
        }

        existing.Reschedule(slot);
        existing.AssignPreset(command.PresetId);

        // A plain block moved under a contract becomes its Bonus block; the block decides. A member
        // stays what it is — the validator has already refused a move out of its contract's period.
        existing.PlaceUnder(_placement.GoverningContract(
            slot, await _contracts.GetAllInEffectAsync(cancellationToken), _clock.TimeZone));

        await _blocks.UpdateAsync(existing, cancellationToken);
        return UpdatePlannedBlockResult.Ok(redemptionCapped);
    }
}
