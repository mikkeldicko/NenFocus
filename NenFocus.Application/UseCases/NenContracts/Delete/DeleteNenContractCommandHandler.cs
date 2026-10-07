using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.NenContracts.Delete;

public sealed class DeleteNenContractCommandHandler : ICommandHandler<DeleteNenContractCommand, DeleteNenContractResult>
{
    private readonly INenContractRepository _contracts;
    private readonly IPlannedBlockRepository _blocks;

    public DeleteNenContractCommandHandler(INenContractRepository contracts, IPlannedBlockRepository blocks)
    {
        _contracts = contracts;
        _blocks = blocks;
    }

    public async Task<DeleteNenContractResult> HandleAsync(DeleteNenContractCommand command, CancellationToken ct = default)
    {
        var contract = await _contracts.GetByIdAsync(command.ContractId, ct);
        if (contract is null)
            return DeleteNenContractResult.Fail("Contract not found.");

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
            {
                block.Leave();
                await _blocks.UpdateAsync(block, ct);
            }
            else
            {
                await _blocks.DeleteAsync(block.Id, ct);
            }
        }

        await _contracts.DeleteAsync(command.ContractId, ct);

        return DeleteNenContractResult.Ok();
    }
}
