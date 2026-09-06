using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.Queries.GetInEffectNenContracts;

public sealed class GetInEffectNenContractsQueryHandler
    : IQueryHandler<GetInEffectNenContractsQuery, IReadOnlyList<NenContractSummaryDto>>
{
    private readonly INenContractRepository _contracts;

    public GetInEffectNenContractsQueryHandler(INenContractRepository contracts)
        => _contracts = contracts;

    public async Task<IReadOnlyList<NenContractSummaryDto>> HandleAsync(
        GetInEffectNenContractsQuery query, CancellationToken cancellationToken = default)
    {
        var inEffect = await _contracts.GetAllInEffectAsync(cancellationToken);

        return inEffect
            .OrderByDescending(c => c.Status == NenContractStatus.Active)
            .ThenBy(c => c.StartDate)
            .Select(c => new NenContractSummaryDto
            {
                Id = c.Id,
                Name = c.Name,
                StartDate = c.StartDate,
                EndDate = c.EndDate,
                StrictnessRule = c.StrictnessRule,
                Status = c.Status
            })
            .ToList();
    }
}
