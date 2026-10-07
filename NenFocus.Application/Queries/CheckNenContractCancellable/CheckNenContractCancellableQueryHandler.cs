using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.CheckNenContractCancellable;

public sealed class CheckNenContractCancellableQueryHandler
    : IQueryHandler<CheckNenContractCancellableQuery, ContractActionVerdictDto?>
{
    private readonly INenContractRepository _contracts;
    private readonly ISessionRepository _sessions;

    public CheckNenContractCancellableQueryHandler(INenContractRepository contracts, ISessionRepository sessions)
    {
        _contracts = contracts;
        _sessions = sessions;
    }

    public async Task<ContractActionVerdictDto?> HandleAsync(CheckNenContractCancellableQuery query, CancellationToken ct = default)
    {
        var contract = await _contracts.GetByIdAsync(query.ContractId, ct);
        if (contract is not { IsInEffect: true })
            return null;

        var sessionRunning = await _sessions.GetActiveAsync(ct) is not null;
        return ContractActionVerdictDto.From(contract.CheckBreak(sessionRunning));
    }
}
