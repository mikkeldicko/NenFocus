using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
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

        // The contract's own answer, asked first for its sentence; Break asks it again itself, so
        // no caller can skip it (Gap 92, D). With no session running, nothing below can cut one short.
        var sessionRunning = await _sessions.GetActiveAsync(ct) is not null;
        var verdict = contract.CheckBreak(sessionRunning);
        if (!verdict.IsAllowed)
            return BreakNenContractResult.Fail(ContractActionVerdictDto.From(verdict).Message!);

        var now = _clock.Now;
        contract.Break(now, _clock.TimeZone, sessionRunning);

        var linkedBlocks = await _blocks.GetForContractAsync(command.ContractId, ct);

        // The contract answers what happens to each of its blocks (Gap 97); this only does it. A
        // block whose session already ended early is final and keeps its real outcome. Bonus
        // blocks are kept until Gap 108 lets the user choose.
        foreach (var block in linkedBlocks)
        {
            var fate = contract.FateWhenBroken(block, now, keepBonusBlocks: true);

            if (fate == BlockFateOnBreak.Cancel)
                block.MarkCancelled(now);
            else if (fate == BlockFateOnBreak.Release)
                block.Leave();
        }

        // The recap is owed once this is saved: NenContractEndedEventHandler shows it (Gap 110).
        return BreakNenContractResult.Ok();
    }
}
