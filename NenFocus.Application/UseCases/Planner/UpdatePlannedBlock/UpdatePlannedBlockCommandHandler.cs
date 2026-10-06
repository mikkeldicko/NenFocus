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
    private readonly ISystemClock _clock;

    public UpdatePlannedBlockCommandHandler(
        IPlannedBlockRepository blocks,
        IBlockPresetRepository presets,
        INenContractRepository contracts,
        IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> computeRedemptionAttribution,
        IBlockPlacementChecker placement,
        ISystemClock clock)
    {
        _blocks = blocks;
        _presets = presets;
        _contracts = contracts;
        _computeRedemptionAttribution = computeRedemptionAttribution;
        _placement = placement;
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

        // Only a redemption block's coverage is recomputed on an edit, against its own contract
        // (Gap 97): a role never changes, so a Committed, Bonus or plain block never starts
        // claiming a deficit because it was edited. Its length was fixed when it was placed (Gap 95,
        // decision 16) — the validator refuses a resize — so only the entries are recomputed; a
        // block that now covers less than its length keeps the surplus (Redemption Rule 6).
        if (existing is FocusBlock { IsRedemption: true } redemption)
        {
            var existingEntries = redemption.RedemptionEntries
                .Select(RedemptionEntryDto.From)
                .ToList();

            var attribution = await _computeRedemptionAttribution.HandleAsync(
                new ComputeRedemptionAttributionQuery(redemption.Membership!.ContractId, command.PresetId, slot, existing.Id, existingEntries),
                cancellationToken);

            redemption.AssignRedemptionEntries(attribution.Entries
                .Select(e => new RedemptionEntry(e.MissedBlockId, e.MinutesCovered))
                .ToList());
        }

        existing.Reschedule(slot);
        existing.AssignPreset(command.PresetId);

        // A plain block moved under a contract becomes its Bonus block; the block decides. A member
        // stays what it is — the validator has already refused a move out of its contract's period.
        existing.PlaceUnder(_placement.GoverningContract(
            slot, await _contracts.GetAllInEffectAsync(cancellationToken), _clock.TimeZone));

        await _blocks.UpdateAsync(existing, cancellationToken);
        return UpdatePlannedBlockResult.Ok();
    }
}
