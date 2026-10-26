using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Planner.UpdateBlock;

public sealed class UpdateBlockCommandHandler : ICommandHandler<UpdateBlockCommand, UpdateBlockResult>
{
    private readonly IBlockRepository _blocks;
    private readonly IBlockPresetRepository _presets;

    public UpdateBlockCommandHandler(IBlockRepository blocks, IBlockPresetRepository presets)
    {
        _blocks = blocks;
        _presets = presets;
    }

    public async Task<UpdateBlockResult> HandleAsync(UpdateBlockCommand command, CancellationToken cancellationToken = default)
    {
        var existing = await _blocks.GetByIdAsync(command.BlockId, cancellationToken);
        if (existing is null)
            return UpdateBlockResult.Fail($"Planned block '{command.BlockId}' not found.");

        var preset = await _presets.GetByIdAsync(command.PresetId, cancellationToken);
        if (preset is null)
            return UpdateBlockResult.Fail($"Preset '{command.PresetId}' not found.");

        // A move never changes membership (Gap 106, decision 18) — the validator refuses a plain
        // block moved where a contract governs, and a member moved out of its contract — and a
        // redemption block keeps its receipt (decision 3). The day badges follow wherever it sits.
        existing.Reschedule(command.Slot);
        existing.AssignPreset(command.PresetId);
        return UpdateBlockResult.Ok();
    }
}
