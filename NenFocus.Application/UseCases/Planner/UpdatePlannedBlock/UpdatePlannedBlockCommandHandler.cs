using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeRedemptionAttribution;
using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;

public sealed class UpdatePlannedBlockCommandHandler : ICommandHandler<UpdatePlannedBlockCommand, UpdatePlannedBlockResult>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockPresetRepository _presets;
    private readonly IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> _computeRedemptionAttribution;

    public UpdatePlannedBlockCommandHandler(
        IPlannedBlockRepository blocks,
        IBlockPresetRepository presets,
        IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> computeRedemptionAttribution)
    {
        _blocks = blocks;
        _presets = presets;
        _computeRedemptionAttribution = computeRedemptionAttribution;
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

        IReadOnlyList<RedemptionEntry> redemptionEntries = [];
        var redemptionCapped = false;

        if (existing is FocusBlock existingFocusBlock)
        {
            // Recomputed fresh on every edit, scoped only to the block's own already-known
            // contract — never re-parented to a different one via an ordinary edit (see
            // UpdatePlannedBlockCommand). A standalone block (ContractId null) stays
            // standalone; editing it never auto-discovers a contract for the first time.
            // Attribution returns the final (capped) window — the one place the cap is computed,
            // so the validator's placement check and this save can never disagree (Gap 88 phase 8).
            if (existingFocusBlock.ContractId is { } contractId)
            {
                var existingEntries = existingFocusBlock.RedemptionEntries
                    .Select(RedemptionEntryDto.From)
                    .ToList();

                var attribution = await _computeRedemptionAttribution.HandleAsync(
                    new ComputeRedemptionAttributionQuery(contractId, command.PresetId, slot, existing.Id, existingEntries),
                    cancellationToken);
                redemptionEntries = attribution.Entries
                    .Select(e => new RedemptionEntry(e.MissedBlockId, e.MinutesCovered))
                    .ToList();
                slot = attribution.Slot;
                redemptionCapped = attribution.WasCapped;
            }
        }

        existing.Reschedule(slot);
        existing.AssignPreset(command.PresetId);

        if (existing is FocusBlock focusBlockToUpdate)
            focusBlockToUpdate.AssignRedemptionEntries(redemptionEntries);

        await _blocks.UpdateAsync(existing, cancellationToken);
        return UpdatePlannedBlockResult.Ok(redemptionCapped);
    }
}
