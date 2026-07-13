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
        await _presets.DeleteAsync(command.PresetId, cancellationToken);
        return DeleteContractPresetResult.Ok();
    }
}
