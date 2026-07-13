using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.NenContracts.Break;

public sealed class BreakNenContractCommandHandler : ICommandHandler<BreakNenContractCommand, BreakNenContractResult>
{
    private readonly INenContractRepository _contracts;
    private readonly IPlannedBlockRepository _blocks;
    private readonly ISystemClock _clock;

    public BreakNenContractCommandHandler(
        INenContractRepository contracts,
        IPlannedBlockRepository blocks,
        ISystemClock clock)
    {
        _contracts = contracts;
        _blocks = blocks;
        _clock = clock;
    }

    public async Task<BreakNenContractResult> HandleAsync(BreakNenContractCommand command, CancellationToken ct = default)
    {
        var contract = await _contracts.GetByIdAsync(command.ContractId, ct);
        if (contract is null)
            return BreakNenContractResult.Fail("Contract not found.");

        var now = _clock.UtcNow;
        contract.Break(now);
        await _contracts.SaveAsync(contract, ct);

        var linkedBlocks = await _blocks.GetForContractAsync(command.ContractId, ct);
        foreach (var block in linkedBlocks.Where(b => !b.HasEnded(now)))
            await _blocks.DeleteAsync(block.Id, ct);

        return BreakNenContractResult.Ok();
    }
}
