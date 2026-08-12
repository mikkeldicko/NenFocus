using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.NenContracts.Delete;

public sealed record DeleteNenContractResult : ICommandResult<DeleteNenContractResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static DeleteNenContractResult Ok() => new() { Success = true };
    public static DeleteNenContractResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
