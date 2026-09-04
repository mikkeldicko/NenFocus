using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.NenContracts.CompleteNenContract;

public sealed record CompleteNenContractResult : ICommandResult<CompleteNenContractResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static CompleteNenContractResult Ok() => new() { Success = true };
    public static CompleteNenContractResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
