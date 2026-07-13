using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.NenContracts.Seal;

public sealed record SealNenContractResult : ICommandResult<SealNenContractResult>
{
    public bool Success { get; init; }
    public Guid? ContractId { get; init; }
    public string? ErrorMessage { get; init; }

    public static SealNenContractResult Ok(Guid contractId) => new() { Success = true, ContractId = contractId };
    public static SealNenContractResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
