using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.ContractPresets.UpdateContractPreset;

public sealed record UpdateContractPresetResult : ICommandResult<UpdateContractPresetResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static UpdateContractPresetResult Ok() =>
        new() { Success = true };
    public static UpdateContractPresetResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
