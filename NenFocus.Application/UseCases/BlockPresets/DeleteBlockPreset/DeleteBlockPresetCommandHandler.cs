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
        await _presets.DeleteAsync(command.PresetId, cancellationToken);
        return DeleteBlockPresetResult.Ok();
    }
}