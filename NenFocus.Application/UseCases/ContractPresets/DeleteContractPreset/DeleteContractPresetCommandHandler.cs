using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.ContractPresets.DeleteContractPreset;

public sealed class DeleteContractPresetCommandHandler
    : ICommandHandler<DeleteContractPresetCommand, DeleteContractPresetResult>
{
    private readonly IContractPresetRepository _presets;

    public DeleteContractPresetCommandHandler(IContractPresetRepository presets)
    {
        _presets = presets;
    }

    public async Task<DeleteContractPresetResult> HandleAsync(
        DeleteContractPresetCommand command,
        CancellationToken cancellationToken = default)
    {
        // Already gone is done: removing is idempotent (Gap 110).
        if (await _presets.GetByIdAsync(command.PresetId, cancellationToken) is { } preset)
            _presets.Remove(preset);
        return DeleteContractPresetResult.Ok();
    }
}
