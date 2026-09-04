using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.UseCases.NenContracts.SaveReflection;

public sealed class SaveNenContractReflectionCommandHandler
    : ICommandHandler<SaveNenContractReflectionCommand, SaveNenContractReflectionResult>
{
    private readonly INenContractRepository _contracts;

    public SaveNenContractReflectionCommandHandler(INenContractRepository contracts)
    {
        _contracts = contracts;
    }

    public async Task<SaveNenContractReflectionResult> HandleAsync(SaveNenContractReflectionCommand command, CancellationToken ct = default)
    {
        var contract = await _contracts.GetByIdAsync(command.ContractId, ct);
        if (contract is null) return SaveNenContractReflectionResult.Fail("Contract not found.");

        if (contract.Status != NenContractStatus.Completed && contract.Status != NenContractStatus.Broken)
            return SaveNenContractReflectionResult.Fail("Reflection can only be set on a completed or broken contract.");

        contract.SetReflection(command.Reflection);

        await _contracts.SaveAsync(contract, ct);
        return SaveNenContractReflectionResult.Ok();
    }
}
