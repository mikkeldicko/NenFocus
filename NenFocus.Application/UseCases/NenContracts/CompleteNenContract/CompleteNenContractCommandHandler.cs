using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractRecap;

namespace NenFocus.Application.UseCases.NenContracts.CompleteNenContract;

public sealed class CompleteNenContractCommandHandler
    : ICommandHandler<CompleteNenContractCommand, CompleteNenContractResult>
{
    private readonly INenContractRepository _contracts;
    private readonly ISystemClock _clock;
    private readonly IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?> _recap;

    public CompleteNenContractCommandHandler(
        INenContractRepository contracts,
        ISystemClock clock,
        IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?> recap)
    {
        _contracts = contracts;
        _clock = clock;
        _recap = recap;
    }

    public async Task<CompleteNenContractResult> HandleAsync(CompleteNenContractCommand command, CancellationToken ct = default)
    {
        var contract = await _contracts.GetByIdAsync(command.ContractId, ct);
        if (contract is null) return CompleteNenContractResult.Fail("Contract not found.");

        // EndDate is deliberately left untouched — historically accurate ("sealed for 10
        // days, finished by day 7"), and NenContract.IsInEffect (Scheduled/Active only)
        // already governs the date-range-overlap check, so completing here frees the
        // calendar for a new contract immediately regardless of the old EndDate.
        contract.Complete(_clock.UtcNow);

        await _contracts.SaveAsync(contract, ct);

        // Flows straight into the Post-Contract overlay immediately, mirroring
        // EndSessionCommandHandler's own PostSessionDto precedent — same query the
        // heartbeat's passive-completion path also uses, so the two agree by construction.
        var recap = await _recap.HandleAsync(new GetNenContractRecapQuery(contract.Id), ct);
        return CompleteNenContractResult.Ok(recap!);
    }
}
