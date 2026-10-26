using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.NenContracts.Delete;

public sealed class DeleteNenContractCommandHandler : ICommandHandler<DeleteNenContractCommand, DeleteNenContractResult>
{
    private readonly INenContractRepository _contracts;
    private readonly IBlockRepository _blocks;

    public DeleteNenContractCommandHandler(INenContractRepository contracts, IBlockRepository blocks)
    {
        _contracts = contracts;
        _blocks = blocks;
    }

    public async Task<DeleteNenContractResult> HandleAsync(DeleteNenContractCommand command, CancellationToken ct = default)
    {
        var contract = await _contracts.GetByIdAsync(command.ContractId, ct);
        if (contract is null)
            return DeleteNenContractResult.Ok(); // already gone is done (Gap 110)

        if (contract.CheckDelete() is { IsAllowed: false } verdict)
            return DeleteNenContractResult.Fail(ContractActionVerdictDto.From(verdict).Message!);

        // A Scheduled contract's StartDate hasn't arrived yet, so none of its blocks can
        // have already ended — its drafted blocks never ran and are deleted with it, no
        // HasEnded distinction needed (unlike Break's cleanup of an Active contract's
        // already-elapsed history). A Bonus block was added on its own, not drafted: deleting
        // the contract never destroys it — it stays as a plain block (Gap 97).
        var linkedBlocks = await _blocks.GetForContractAsync(command.ContractId, ct);
        foreach (var block in linkedBlocks)
        {
            if (block.Membership is { IsBonus: true })
                block.Leave();
            else
                _blocks.Remove(block);
        }

        _contracts.Remove(contract);

        return DeleteNenContractResult.Ok();
    }
}
