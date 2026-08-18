using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;

namespace NenFocus.Application.UseCases.NenContracts.Break;

public sealed class BreakNenContractCommandHandler : ICommandHandler<BreakNenContractCommand, BreakNenContractResult>
{
    private readonly INenContractRepository _contracts;
    private readonly IPlannedBlockRepository _blocks;
    private readonly ISessionRepository _sessions;
    private readonly ISystemClock _clock;

    public BreakNenContractCommandHandler(
        INenContractRepository contracts,
        IPlannedBlockRepository blocks,
        ISessionRepository sessions,
        ISystemClock clock)
    {
        _contracts = contracts;
        _blocks = blocks;
        _sessions = sessions;
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
        var cancelledFocusBlockIds = new HashSet<Guid>();

        foreach (var block in linkedBlocks.Where(b => !b.HasEnded(now)))
        {
            block.MarkCancelled(now);
            await _blocks.SaveAsync(block, ct);

            if (block is FocusBlock focusBlock)
                cancelledFocusBlockIds.Add(focusBlock.Id);
        }

        var activeSession = await _sessions.GetActiveAsync(ct);
        if (activeSession is { OriginatingBlockId: { } originatingBlockId } &&
            cancelledFocusBlockIds.Contains(originatingBlockId))
        {
            activeSession.Complete(now);
        }

        return BreakNenContractResult.Ok();
    }
}
