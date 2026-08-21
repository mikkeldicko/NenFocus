using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractDeficits;
using NenFocus.Domain.Enums;

namespace NenFocus.Application.Queries.FindRedemptionEligibleBlock;

public sealed class FindRedemptionEligibleBlockQueryHandler
    : IQueryHandler<FindRedemptionEligibleBlockQuery, FocusBlockDeficitDto?>
{
    private readonly IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> _deficits;
    private readonly INenContractRepository _contracts;

    public FindRedemptionEligibleBlockQueryHandler(
        IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> deficits,
        INenContractRepository contracts)
    {
        _deficits = deficits;
        _contracts = contracts;
    }

    public async Task<FocusBlockDeficitDto?> HandleAsync(
        FindRedemptionEligibleBlockQuery query,
        CancellationToken cancellationToken = default)
    {
        var contract = await _contracts.GetByIdAsync(query.ContractId, cancellationToken);
        if (contract is null) return null;

        var deficits = await _deficits.HandleAsync(new GetNenContractDeficitsQuery(query.ContractId), cancellationToken);
        var proposedDate = DateOnly.FromDateTime(query.ProposedDate);

        return deficits
            .Where(d => d.BlockPresetId == query.PresetId)
            .Where(d => contract.CheckRedemptionEligibility(DateOnly.FromDateTime(d.ScheduledStart), proposedDate) == RedemptionEligibility.Allowed)
            .OrderBy(d => d.ScheduledStart)
            .FirstOrDefault();
    }
}
