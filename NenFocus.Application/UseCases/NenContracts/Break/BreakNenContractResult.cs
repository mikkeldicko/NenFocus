using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.NenContracts.Break;

public sealed record BreakNenContractResult : ICommandResult<BreakNenContractResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public NenContractRecapDto? Recap { get; init; }

    public static BreakNenContractResult Ok(NenContractRecapDto recap) =>
        new() { Success = true, Recap = recap };
    public static BreakNenContractResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
