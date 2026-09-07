using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractRecap;
using NenFocus.Domain.Entities;

namespace NenFocus.Application.UseCases.NenContracts.Break;

public sealed class BreakNenContractCommandHandler : ICommandHandler<BreakNenContractCommand, BreakNenContractResult>
{
    private readonly INenContractRepository _contracts;
    private readonly IPlannedBlockRepository _blocks;
    private readonly ISessionRepository _sessions;
    private readonly ISystemClock _clock;
    private readonly IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?> _recap;

    public BreakNenContractCommandHandler(
        INenContractRepository contracts,
        IPlannedBlockRepository blocks,
        ISessionRepository sessions,
        ISystemClock clock,
        IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?> recap)
    {
        _contracts = contracts;
        _blocks = blocks;
        _sessions = sessions;
        _clock = clock;
        _recap = recap;
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

        // Flows straight into the Post-Contract overlay immediately, same as
        // CompleteNenContractCommandHandler's own precedent — Break is a direct synchronous
        // UI action, so its recap never needs the heartbeat's live-event path.
        var recap = await _recap.HandleAsync(new GetNenContractRecapQuery(contract.Id), ct);
        return BreakNenContractResult.Ok(recap!);
    }
}
