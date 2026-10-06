using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.Queries.GetNenContractCompletionReadiness;

/// <summary>
/// Gathers what NenContract.CheckComplete needs — its blocks, the sessions on its days, whether any
/// session is running — and returns its answer (Gap 95). The Complete validator, the Complete
/// handler and the Planner's "Complete Contract" button all read this one answer.
/// </summary>
public sealed class GetNenContractCompletionReadinessQueryHandler
    : IQueryHandler<GetNenContractCompletionReadinessQuery, NenContractCompletionReadinessDto>
{
    private readonly INenContractRepository _contracts;
    private readonly IPlannedBlockRepository _blocks;
    private readonly ISessionRepository _sessions;
    private readonly ISystemClock _clock;

    public GetNenContractCompletionReadinessQueryHandler(
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

    public async Task<NenContractCompletionReadinessDto> HandleAsync(
        GetNenContractCompletionReadinessQuery query, CancellationToken ct = default)
    {
        var contract = await _contracts.GetByIdAsync(query.ContractId, ct);
        if (contract is null || contract.Status != NenContractStatus.Active)
            return NenContractCompletionReadinessDto.NotActive;

        // Every member, Bonus blocks included (Gap 97): completing must leave nothing to clean up.
        var members = await _blocks.GetForContractAsync(contract.Id, ct);
        var sessions = await _sessions.GetHistoryAsync(
            _clock.TimeZone.SlotCovering(contract.StartDate, contract.EndDate), presetId: null, ct);
        var sessionRunning = await _sessions.GetActiveAsync(ct) is not null;

        var verdict = contract.CheckComplete(members, sessions, sessionRunning, _clock.Now, _clock.TimeZone);
        return NenContractCompletionReadinessDto.From(verdict);
    }
}
