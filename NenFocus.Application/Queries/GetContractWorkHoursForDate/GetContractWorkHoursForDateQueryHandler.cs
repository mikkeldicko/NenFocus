using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetContractWorkHoursForDate;

public sealed class GetContractWorkHoursForDateQueryHandler : IQueryHandler<GetContractWorkHoursForDateQuery, ContractWorkHoursDto?>
{
    private readonly INenContractRepository _contracts;

    public GetContractWorkHoursForDateQueryHandler(INenContractRepository contracts) => _contracts = contracts;

    public async Task<ContractWorkHoursDto?> HandleAsync(GetContractWorkHoursForDateQuery query, CancellationToken cancellationToken = default)
    {
        var contract = await _contracts.GetByIdAsync(query.ContractId, cancellationToken);
        return contract?.WindowOn(query.Date) is { } window ? new ContractWorkHoursDto(window.Start, window.End) : null;
    }
}
