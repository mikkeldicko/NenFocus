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

        // A Scheduled contract's StartDate hasn't arrived yet, so none of its blocks can
        // have already ended — every linked block was drafted for this contract and never
        // ran; all of them are deleted, no HasEnded distinction needed (unlike Break's
        // cleanup of an Active contract's already-elapsed history).
        var linkedBlocks = await _blocks.GetForContractAsync(command.ContractId, ct);
        foreach (var block in linkedBlocks)
            await _blocks.DeleteAsync(block.Id, ct);

        await _contracts.DeleteAsync(command.ContractId, ct);

        return DeleteNenContractResult.Ok();
    }
}
