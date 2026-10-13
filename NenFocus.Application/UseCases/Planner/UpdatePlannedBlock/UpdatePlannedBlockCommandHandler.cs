using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.UpdatePlannedBlock;

public sealed class UpdatePlannedBlockCommandHandler : ICommandHandler<UpdatePlannedBlockCommand, UpdatePlannedBlockResult>
{
    private readonly IPlannedBlockRepository _blocks;
    private readonly IBlockPresetRepository _presets;

    public UpdatePlannedBlockCommandHandler(IPlannedBlockRepository blocks, IBlockPresetRepository presets)
    {
        _blocks = blocks;
        _presets = presets;
    }

    public async Task<UpdatePlannedBlockResult> HandleAsync(UpdatePlannedBlockCommand command, CancellationToken cancellationToken = default)
    {
        var existing = await _blocks.GetByIdAsync(command.BlockId, cancellationToken);
        if (existing is null)
            return UpdatePlannedBlockResult.Fail($"Planned block '{command.BlockId}' not found.");

        var preset = await _presets.GetByIdAsync(command.PresetId, cancellationToken);
        if (preset is null)
            return UpdatePlannedBlockResult.Fail($"Preset '{command.PresetId}' not found.");

        // A move never changes membership (Gap 106, decision 18) — the validator refuses a plain
        // block moved where a contract governs, and a member moved out of its contract — and a
        // redemption block keeps its receipt (decision 3). The day badges follow wherever it sits.
        existing.Reschedule(command.Slot);
        existing.AssignPreset(command.PresetId);
        return UpdatePlannedBlockResult.Ok();
    }
}
