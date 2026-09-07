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
        // Deliberately every contract regardless of status, not just GetAllInEffectAsync's
        // Scheduled/Active set — a Completed or Broken contract still occupies the calendar
        // days it actually ran (EndDate shrinks to reflect that for a broken one, see
        // NenContract.Break), and a new contract must not be allowed to start on a day
        // another one — however it ended — was still in effect on. See ARCHITECT_GOVERNANCE.md
        // Open Gap L.
        var allContracts = await _contracts.GetAllAsync(cancellationToken);

        var overlapping = allContracts.FirstOrDefault(c =>
            c.Id != query.ExcludeContractId &&
            query.StartDate <= c.EndDate && query.EndDate >= c.StartDate);

        return overlapping?.Name;
    }
}
