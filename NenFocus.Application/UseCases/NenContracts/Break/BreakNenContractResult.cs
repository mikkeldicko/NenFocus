using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.NenContracts.Break;

public sealed record BreakNenContractResult : ICommandResult<BreakNenContractResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static BreakNenContractResult Ok() => new() { Success = true };
    public static BreakNenContractResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
