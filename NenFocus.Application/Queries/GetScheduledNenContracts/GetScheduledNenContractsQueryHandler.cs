using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.Queries.GetScheduledNenContracts;

public sealed class GetScheduledNenContractsQueryHandler
    : IQueryHandler<GetScheduledNenContractsQuery, IReadOnlyList<ScheduledNenContractDto>>
{
    private readonly INenContractRepository _contracts;

    public GetScheduledNenContractsQueryHandler(INenContractRepository contracts)
        => _contracts = contracts;

    public async Task<IReadOnlyList<ScheduledNenContractDto>> HandleAsync(
        GetScheduledNenContractsQuery query, CancellationToken cancellationToken = default)
    {
        var inEffect = await _contracts.GetAllInEffectAsync(cancellationToken);

        return inEffect
            .Where(c => c.Status == NenContractStatus.Scheduled)
            .OrderBy(c => c.StartDate)
            .Select(c => new ScheduledNenContractDto
            {
                Id = c.Id,
                Name = c.Name,
                StartDate = c.StartDate,
                EndDate = c.EndDate,
                StrictnessRule = c.StrictnessRule
            })
            .ToList();
    }
}
