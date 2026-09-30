using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeRedemptionAttribution;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.Planner.CreatePlannedBlock;

public sealed class CreatePlannedBlockCommandHandler : ICommandHandler<CreatePlannedBlockCommand, CreatePlannedBlockResult>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockPresetRepository _presets;
    private readonly IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> _computeRedemptionAttribution;

    public CreatePlannedBlockCommandHandler(
        IPlannedBlockRepository blocks,
        IBlockPresetRepository presets,
        IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> computeRedemptionAttribution)
    {
        _blocks = blocks;
        _presets = presets;
        _computeRedemptionAttribution = computeRedemptionAttribution;
    }

    public async Task<CreatePlannedBlockResult> HandleAsync(CreatePlannedBlockCommand command, CancellationToken cancellationToken = default)
    {
        var slot = command.Slot;

        var preset = await _presets.GetByIdAsync(command.PresetId, cancellationToken);
        if (preset is null) return CreatePlannedBlockResult.Fail("Preset not found.");

        Guid? contractId = command.ContractId;
        IReadOnlyList<RedemptionEntry> redemptionEntries = [];
        var redemptionCapped = false;

        if (command.BlockPresetType == BlockPresetType.Focus && !command.DeclineRedemption)
        {
            // Attribution returns the final (capped) window — the one place the cap is computed,
            // so the validator's placement check and this save can never disagree (Gap 88 phase 8).
            var attribution = await _computeRedemptionAttribution.HandleAsync(
                new ComputeRedemptionAttributionQuery(command.ContractId, command.PresetId, slot, ExcludeBlockId: null),
                cancellationToken);
            contractId = attribution.ContractId;
            redemptionEntries = attribution.Entries
                .Select(e => new RedemptionEntry(e.MissedBlockId, e.MinutesCovered))
                .ToList();
            slot = attribution.Slot;
            redemptionCapped = attribution.WasCapped;
        }

        PlannedBlock block;
        if (command.BlockPresetType == BlockPresetType.Focus)
        {
            block = FocusBlock.Create(Guid.NewGuid(), command.PresetId, slot,
                contractId: contractId,
                redemptionEntries: redemptionEntries);
        }
        else
        {
            block = BackgroundBlock.Create(Guid.NewGuid(), command.PresetId, slot,
                contractId: command.ContractId);
        }

        await _blocks.AddAsync(block, cancellationToken);
        return CreatePlannedBlockResult.Ok(block.Id, redemptionCapped);
    }
}
