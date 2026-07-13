using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.ContractPresets.DeleteContractPreset;

public sealed record DeleteContractPresetCommand : ICommand<DeleteContractPresetResult>
{
    public Guid PresetId { get; init; }
}
