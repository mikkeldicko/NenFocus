using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.NenContracts.Extend;

public sealed record ExtendNenContractResult : ICommandResult<ExtendNenContractResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static ExtendNenContractResult Ok() => new() { Success = true };
    public static ExtendNenContractResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
