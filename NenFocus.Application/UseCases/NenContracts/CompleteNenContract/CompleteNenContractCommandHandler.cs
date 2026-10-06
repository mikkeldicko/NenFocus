using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractCompletionReadiness;
using NenFocus.Application.Queries.GetNenContractRecap;

namespace NenFocus.Application.UseCases.NenContracts.CompleteNenContract;

public sealed class CompleteNenContractCommandHandler
    : ICommandHandler<CompleteNenContractCommand, CompleteNenContractResult>
{
    private readonly INenContractRepository _contracts;
    private readonly ISystemClock _clock;
    private readonly IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?> _recap;
    private readonly IQueryHandler<GetNenContractCompletionReadinessQuery, NenContractCompletionReadinessDto> _readiness;

    public CompleteNenContractCommandHandler(
        INenContractRepository contracts,
        ISystemClock clock,
        IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?> recap,
        IQueryHandler<GetNenContractCompletionReadinessQuery, NenContractCompletionReadinessDto> readiness)
    {
        _contracts = contracts;
        _clock = clock;
        _recap = recap;
        _readiness = readiness;
    }

    public async Task<CompleteNenContractResult> HandleAsync(CompleteNenContractCommand command, CancellationToken ct = default)
    {
        var contract = await _contracts.GetByIdAsync(command.ContractId, ct);
        if (contract is null) return CompleteNenContractResult.Fail("Contract not found.");

        // The contract's own answer again, so a caller that skipped the validator still can't
        // complete it mid-session or with hours owed (Gap 95).
        var readiness = await _readiness.HandleAsync(new GetNenContractCompletionReadinessQuery(contract.Id), ct);
        if (!readiness.IsReady)
            return CompleteNenContractResult.Fail(readiness.NotReadyReason!);

        // EndDate is deliberately left untouched — historically accurate ("sealed for 10
        // days, finished by day 7"), and NenContract.IsInEffect (Scheduled/Active only)
        // already governs the date-range-overlap check, so completing here frees the
        // calendar for a new contract immediately regardless of the old EndDate.
        contract.Complete(_clock.Now);

        await _contracts.SaveAsync(contract, ct);

        // Flows straight into the Post-Contract overlay immediately, mirroring
        // EndSessionCommandHandler's own PostSessionDto precedent — same query the
        // heartbeat's passive-completion path also uses, so the two agree by construction.
        var recap = await _recap.HandleAsync(new GetNenContractRecapQuery(contract.Id), ct);
        return CompleteNenContractResult.Ok(recap!);
    }
}
