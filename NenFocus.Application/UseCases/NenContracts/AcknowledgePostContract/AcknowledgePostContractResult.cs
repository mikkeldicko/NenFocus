using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.NenContracts.AcknowledgePostContract;

public sealed record AcknowledgePostContractResult : ICommandResult<AcknowledgePostContractResult>
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static AcknowledgePostContractResult Ok() => new() { Success = true };
    public static AcknowledgePostContractResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
