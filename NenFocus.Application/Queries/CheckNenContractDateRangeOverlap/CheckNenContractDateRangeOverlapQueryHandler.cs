using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.CheckNenContractDateRangeOverlap;

public sealed class CheckNenContractDateRangeOverlapQueryHandler
    : IQueryHandler<CheckNenContractDateRangeOverlapQuery, string?>
{
    private readonly INenContractRepository _contracts;

    public CheckNenContractDateRangeOverlapQueryHandler(INenContractRepository contracts)
        => _contracts = contracts;

    public async Task<string?> HandleAsync(CheckNenContractDateRangeOverlapQuery query, CancellationToken cancellationToken = default)
    {
        var inEffect = await _contracts.GetAllInEffectAsync(cancellationToken);

        var overlapping = inEffect.FirstOrDefault(c =>
            c.Id != query.ExcludeContractId &&
            query.StartDate <= c.EndDate && query.EndDate >= c.StartDate);

        return overlapping?.Name;
    }
}
