using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractRecap;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

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

        // The contract's own answer again, so a caller that skipped the validator still can't
        // break it mid-session (Gap 95). With no session running, nothing below can cut one short.
        var verdict = contract.CheckBreak(sessionRunning: await _sessions.GetActiveAsync(ct) is not null);
        if (!verdict.IsAllowed)
            return BreakNenContractResult.Fail(ContractEndVerdictDto.From(verdict).Message!);

        var now = _clock.Now;
        contract.Break(now, _clock.TimeZone);
        await _contracts.SaveAsync(contract, ct);

        var linkedBlocks = await _blocks.GetForContractAsync(command.ContractId, ct);

        // The contract answers what happens to each of its blocks (Gap 97); this only does it. A
        // block whose session already ended early is final and keeps its real outcome. Bonus
        // blocks are kept until Gap 108 lets the user choose.
        foreach (var block in linkedBlocks)
        {
            var fate = contract.FateWhenBroken(block, now, keepBonusBlocks: true);

            if (fate == BlockFateOnBreak.Cancel)
            {
                block.MarkCancelled(now);
            }
            else if (fate == BlockFateOnBreak.Release)
            {
                block.Leave();
            }
            else
            {
                continue;
            }

            await _blocks.SaveAsync(block, ct);
        }

        // Flows straight into the Post-Contract overlay immediately, same as
        // CompleteNenContractCommandHandler's own precedent — Break is a direct synchronous
        // UI action, so its recap never needs the heartbeat's live-event path.
        var recap = await _recap.HandleAsync(new GetNenContractRecapQuery(contract.Id), ct);
        return BreakNenContractResult.Ok(recap!);
    }
}
