using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.BlockPresets.DeleteBlockPreset;

public sealed class DeleteBlockPresetCommandHandler
    : ICommandHandler<DeleteBlockPresetCommand, DeleteBlockPresetResult>
{
    private readonly IBlockPresetRepository _presets;

    public DeleteBlockPresetCommandHandler(IBlockPresetRepository presets)
    {
        _presets = presets;
    }

    public async Task<DeleteBlockPresetResult> HandleAsync(
        DeleteBlockPresetCommand command,
        CancellationToken cancellationToken = default)
    {
        // Already gone is done: removing is idempotent (Gap 110).
        if (await _presets.GetByIdAsync(command.PresetId, cancellationToken) is { } preset)
            _presets.Remove(preset);
        return DeleteBlockPresetResult.Ok();
    }
}