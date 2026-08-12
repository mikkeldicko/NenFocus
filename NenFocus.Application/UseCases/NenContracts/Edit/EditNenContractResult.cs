using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.NenContracts.Edit;

public sealed record EditNenContractResult : ICommandResult<EditNenContractResult>
{
    public bool Success { get; init; }
    public Guid? ContractId { get; init; }
    public string? ErrorMessage { get; init; }

    public static EditNenContractResult Ok(Guid contractId) => new() { Success = true, ContractId = contractId };
    public static EditNenContractResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
