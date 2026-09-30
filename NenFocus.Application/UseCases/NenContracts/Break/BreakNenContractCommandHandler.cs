using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractRecap;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.UseCases.NenContracts.Break;

public sealed class BreakNenContractCommandHandler : ICommandHandler<BreakNenContractCommand, BreakNenContractResult>
{
    private readonly INenContractRepository _contracts;
    private readonly IPlannedBlockRepository _blocks;
    private readonly ISessionRepository _sessions;
    private readonly ISystemClock _clock;
    private readonly IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?> _recap;
    private readonly IFocusBlockOutcomeClassifier _outcomeClassifier;

    public BreakNenContractCommandHandler(
        INenContractRepository contracts,
        IPlannedBlockRepository blocks,
        ISessionRepository sessions,
        ISystemClock clock,
        IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?> recap,
        IFocusBlockOutcomeClassifier outcomeClassifier)
    {
        _contracts = contracts;
        _blocks = blocks;
        _sessions = sessions;
        _clock = clock;
        _recap = recap;
        _outcomeClassifier = outcomeClassifier;
    }

    public async Task<BreakNenContractResult> HandleAsync(BreakNenContractCommand command, CancellationToken ct = default)
    {
        var contract = await _contracts.GetByIdAsync(command.ContractId, ct);
        if (contract is null)
            return BreakNenContractResult.Fail("Contract not found.");

        var now = _clock.Now;
        contract.Break(now, _clock.TimeZone);
        await _contracts.SaveAsync(contract, ct);

        var linkedBlocks = await _blocks.GetForContractAsync(command.ContractId, ct);
        var cancelledFocusBlockIds = new HashSet<Guid>();

        // IsResolved (not raw HasEnded) so a FocusBlock whose session already ended early
        // keeps its real outcome (EndedEarly/CompletedOnTime/StartedLate) instead of being
        // retroactively relabeled Cancelled just because its raw scheduled End hadn't
        // arrived yet — see PlannedBlockResolutionExtensions.
        foreach (var block in linkedBlocks.Where(b => !b.IsResolved(_outcomeClassifier, now)))
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
