using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.NenContracts.SaveReflection;

public sealed record SaveNenContractReflectionResult : ICommandResult<SaveNenContractReflectionResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static SaveNenContractReflectionResult Ok() => new() { Success = true };
    public static SaveNenContractReflectionResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
