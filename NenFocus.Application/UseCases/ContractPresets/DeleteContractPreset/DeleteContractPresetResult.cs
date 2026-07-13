using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.ContractPresets.DeleteContractPreset;

public sealed record DeleteContractPresetResult : ICommandResult<DeleteContractPresetResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static DeleteContractPresetResult Ok() => new() { Success = true };
    public static DeleteContractPresetResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
