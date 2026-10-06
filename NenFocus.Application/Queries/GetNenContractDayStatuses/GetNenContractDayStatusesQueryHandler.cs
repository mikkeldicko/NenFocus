using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.GetNenContractDayStatuses;

public sealed class GetNenContractDayStatusesQueryHandler
    : IQueryHandler<GetNenContractDayStatusesQuery, IReadOnlyList<ContractDayStatus>>
{
    private readonly INenContractRepository _contracts;
    private readonly IPlannedBlockRepository _blocks;
    private readonly ISessionRepository _sessions;
    private readonly ISystemClock _clock;

    public GetNenContractDayStatusesQueryHandler(
        INenContractRepository contracts,
        IPlannedBlockRepository blocks,
        ISessionRepository sessions,
        ISystemClock clock)
    {
        _contracts = contracts;
        _blocks = blocks;
        _sessions = sessions;
        _clock = clock;
    }

    public async Task<IReadOnlyList<ContractDayStatus>> HandleAsync(
        GetNenContractDayStatusesQuery query, CancellationToken ct = default)
    {
        var contract = await _contracts.GetByIdAsync(query.ContractId, ct);
        if (contract is null)
            return [];

        // Its own blocks, and the sessions on its days — the contract decides which of those count.
        var blocks = await _blocks.GetForContractAsync(contract.Id, ct);
        var sessions = await _sessions.GetHistoryAsync(
            _clock.TimeZone.SlotCovering(contract.StartDate, contract.EndDate), presetId: null, ct);

        return contract.DayStatuses(blocks, sessions, _clock.Now, _clock.TimeZone);
    }
}
