using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.NenContracts.CompleteNenContract;

public sealed class CompleteNenContractCommandHandler
    : ICommandHandler<CompleteNenContractCommand, CompleteNenContractResult>
{
    private readonly INenContractRepository _contracts;
    private readonly IPlannedBlockRepository _blocks;
    private readonly ISessionRepository _sessions;
    private readonly ISystemClock _clock;

    public CompleteNenContractCommandHandler(
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

    public async Task<CompleteNenContractResult> HandleAsync(CompleteNenContractCommand command, CancellationToken ct = default)
    {
        var contract = await _contracts.GetByIdAsync(command.ContractId, ct);
        if (contract is null) return CompleteNenContractResult.Fail("Contract not found.");

        // What the contract needs to answer for itself — the same inputs the readiness query
        // gathers for the Planner's button (Gap 95). Every member, Bonus blocks included (Gap 97).
        var now = _clock.Now;
        var members = await _blocks.GetForContractAsync(contract.Id, ct);
        var sessions = await _sessions.GetHistoryAsync(
            _clock.TimeZone.SlotCovering(contract.StartDate, contract.EndDate), presetId: null, ct);
        var sessionRunning = await _sessions.GetActiveAsync(ct) is not null;

        // Asked first for its sentence; CompleteEarly asks it again itself, so no caller can
        // complete mid-session or with hours owed (Gap 92, D).
        var verdict = contract.CheckComplete(members, sessions, sessionRunning, now, _clock.TimeZone);
        if (!verdict.IsAllowed)
            return CompleteNenContractResult.Fail(ContractActionVerdictDto.From(verdict).Message!);

        // EndDate is deliberately left untouched — historically accurate ("sealed for 10
        // days, finished by day 7"), and NenContract.IsInEffect (Scheduled/Active only)
        // already governs the date-range-overlap check, so completing here frees the
        // calendar for a new contract immediately regardless of the old EndDate.
        contract.CompleteEarly(now, members, sessions, sessionRunning, _clock.TimeZone);


        // The recap is owed once this is saved: NenContractEndedEventHandler shows it (Gap 110).
        return CompleteNenContractResult.Ok();
    }
}
