using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractRecap;

namespace NenFocus.Application.Queries.GetUnacknowledgedPostContracts;

public sealed class GetUnacknowledgedPostContractsQueryHandler
    : IQueryHandler<GetUnacknowledgedPostContractsQuery, IReadOnlyList<NenContractRecapDto>>
{
    private readonly INenContractRepository _contracts;
    private readonly IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?> _recap;

    public GetUnacknowledgedPostContractsQueryHandler(
        INenContractRepository contracts,
        IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?> recap)
    {
        _contracts = contracts;
        _recap = recap;
    }

    public async Task<IReadOnlyList<NenContractRecapDto>> HandleAsync(
        GetUnacknowledgedPostContractsQuery query, CancellationToken ct = default)
    {
        var contracts = await _contracts.GetUnacknowledgedPostContractsAsync(ct);

        var results = new List<NenContractRecapDto>(contracts.Count);
        foreach (var contract in contracts)
        {
            var recap = await _recap.HandleAsync(new GetNenContractRecapQuery(contract.Id), ct);
            if (recap is not null) results.Add(recap);
        }

        return results;
    }
}
