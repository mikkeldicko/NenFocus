using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.ContractPresets.CreateContractPreset;

public sealed record CreateContractPresetResult : ICommandResult<CreateContractPresetResult>
{
    public bool Success { get; init; }
    public Guid? PresetId { get; init; }
    public string? ErrorMessage { get; init; }

    public static CreateContractPresetResult Ok(Guid id) =>
        new() { Success = true, PresetId = id };
    public static CreateContractPresetResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
